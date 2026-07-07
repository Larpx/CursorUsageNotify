using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using CursorUsageNotify.Core;
using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.Models.Dtos;
using CursorUsageNotify.Models.Entities;
using CursorUsageNotify.Services.Http;
using CursorUsageNotify.Services.Messages;
using CursorUsageNotify.Services.Notifications;
using CursorUsageNotify.Services.Security;
using CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CursorUsageNotify.Services.Scheduling;

/// <summary>
/// 后台定时同步服务：周期性拉取 Cursor 用量数据入库，并触发 Toast 通知。
/// 通过 PeriodicTimer 控制两个独立循环（拉取 + 通知），避免引入 Quartz 复杂度。
/// </summary>
public sealed class UsageSyncHostedService : BackgroundService
{
    private readonly ICursorApiClient _apiClient;
    private readonly IUsageRepository _repository;
    private readonly INotificationService _notification;
    private readonly IMessenger _messenger;
    private readonly UsageSyncOptions _options;
    private readonly TokenProtector _protector;
    private readonly AppSettings _settings;
    private readonly ILogger<UsageSyncHostedService> _logger;

    public UsageSyncHostedService(
        ICursorApiClient apiClient,
        IUsageRepository repository,
        INotificationService notification,
        IMessenger messenger,
        UsageSyncOptions options,
        TokenProtector protector,
        AppSettings settings,
        ILogger<UsageSyncHostedService> logger)
    {
        _apiClient = apiClient;
        _repository = repository;
        _notification = notification;
        _messenger = messenger;
        _options = options;
        _protector = protector;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("用量同步服务已启动");

        // 启动时从 secrets.dat 加载已保存的 token
        await LoadSavedTokenAsync();

        // 订阅托盘指令
        _messenger.Register<ToggleSyncMessage>(this, (_, m) => _options.IsRunning = m.IsRunning);
        _messenger.Register<TriggerSyncNowMessage>(this, async (_, _) => await SafeSyncAsync(stoppingToken));

        // 启动时立即跑一次
        await SafeSyncAsync(stoppingToken);

        // 双循环：拉取 + 通知
        var syncTask = RunSyncLoopAsync(stoppingToken);
        var notifyTask = RunNotifyLoopAsync(stoppingToken);

        await Task.WhenAll(syncTask, notifyTask);
    }

    private async Task RunSyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var interval = Math.Max(1, _options.SyncIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(interval), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_options.IsRunning && !string.IsNullOrEmpty(_options.SessionToken))
            {
                await SafeSyncAsync(ct);
            }
        }
    }

    private async Task RunNotifyLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var interval = Math.Max(1, _options.NotificationIntervalMinutes);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(interval), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_options.IsRunning)
            {
                await SafeNotifyAsync(ct);
            }
        }
    }

    private async Task SafeSyncAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.SessionToken))
        {
            _logger.LogWarning("跳过同步：session token 为空");
            return;
        }

        // 通知 UI 同步开始（显示进度条，避免误以为卡顿）
        _messenger.Send(new SyncStartedMessage());

        try
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startTs = _options.LastSyncTimeMs > 0 ? _options.LastSyncTimeMs : 0;

            var allEvents = new List<CursorUsageEventDto>();
            var page = 1;
            const int pageSize = 500;
            int total;

            do
            {
                var resp = await _apiClient.GetFilteredUsageEventsAsync(
                    _options.SessionToken, page, pageSize, startTs, ct);
                allEvents.AddRange(resp.UsageEventsDisplay);
                total = resp.TotalUsageEventsCount;
                page++;
            } while (allEvents.Count < total && page < 50); // 安全上限 50 页

            var entities = allEvents.Select(MapToEntity).ToList();
            var inserted = await _repository.UpsertUsageEventsAsync(entities, ct);

            // 拉取计费周期汇总（GET，字段基于实际抓包：billingCycleStart/planUsage）
            CursorPeriodUsageDto? periodDto = null;
            try
            {
                periodDto = await _apiClient.GetCurrentPeriodUsageAsync(_options.SessionToken, ct);
            }
            catch (CursorApiAuthException)
            {
                throw; // 401 向上抛，触发 cookie 失效通知
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "拉取计费周期汇总失败（不影响事件入库）");
            }

            // 并行拉取多个辅助 GET API：stripe 订阅 / 用户资料 / 计费周期 / 发票 / 会话
            CursorStripeSubscriptionDto? stripeDto = null;
            CursorUserProfileDto? profileDto = null;
            CursorBillingCycleDto? cycleDto = null;
            CursorInvoicesDto? invoicesDto = null;
            CursorSessionsDto? sessionsDto = null;
            try
            {
                stripeDto = await _apiClient.GetStripeSubscriptionAsync(_options.SessionToken, ct);
                profileDto = await _apiClient.GetUserProfileAsync(_options.SessionToken, ct);
                cycleDto = await _apiClient.GetBillingCycleAsync(_options.SessionToken, ct);
                invoicesDto = await _apiClient.ListInvoicesAsync(_options.SessionToken, ct);
                sessionsDto = await _apiClient.GetSessionsAsync(_options.SessionToken, ct);
            }
            catch (CursorApiAuthException)
            {
                throw; // 401 向上抛，触发 cookie 失效通知
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "拉取订阅/账单辅助 API 失败（不影响事件入库）");
            }

            // cookie 过期检测：会话即将过期时通知 UI 提醒用户更新
            CheckSessionExpiry(sessionsDto);

            // period 实体入库（planName 取自 stripe.membershipType）
            var planName = stripeDto?.MembershipType;
            if (periodDto is not null)
            {
                await _repository.UpsertPeriodUsageAsync(MapToPeriodEntity(periodDto, nowMs, planName), ct);
            }

            // 订阅信息入库
            SubscriptionEntity? sub = null;
            if (stripeDto is not null || cycleDto is not null || invoicesDto is not null)
            {
                sub = MapToSubscriptionEntity(stripeDto, cycleDto, invoicesDto, profileDto, periodDto, nowMs);
                await _repository.UpsertSubscriptionAsync(sub, ct);
            }

            // 用户信息（优先 profile.handle/displayName，其次事件表邮箱，再其次 subscription）
            var userHandle = profileDto?.Profile?.Handle;
            var userDisplay = profileDto?.Profile?.DisplayName;
            var userEmail = userHandle
                            ?? allEvents.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.UserEmail))?.UserEmail
                            ?? sub?.Email;

            if (userEmail is not null)
            {
                await _repository.UpsertUserInfoAsync(new UserInfoEntity
                {
                    Email = userEmail,
                    Name = userDisplay ?? userHandle,
                    PlanName = planName,
                    MonthlyLimitDollars = 0,
                    HardLimitOverrideDollars = 0,
                    SnapshotTime = nowMs
                }, ct);
            }

            _options.LastSyncTimeMs = nowMs;

            var latestPeriod = await _repository.GetLatestPeriodUsageAsync(ct);
            var latestUser = await _repository.GetLatestUserInfoAsync(ct);
            var latestSub = await _repository.GetLatestSubscriptionAsync(ct);

            // 按订阅周期从 events 表聚合统计
            var aggPeriodStart = latestSub?.CurrentPeriodStart
                                 ?? latestPeriod?.PeriodStart
                                 ?? allEvents.MinBy(e => e.TryGetTimestampMs())?.TryGetTimestampMs()
                                 ?? 0;
            var aggPeriodEnd = latestSub?.CurrentPeriodEnd
                               ?? latestPeriod?.PeriodEnd
                               ?? allEvents.MaxBy(e => e.TryGetTimestampMs())?.TryGetTimestampMs()
                               ?? 0;

            var aggStats = aggPeriodStart > 0 && aggPeriodEnd > 0
                ? await _repository.AggregateStatsAsync(aggPeriodStart, aggPeriodEnd, ct)
                : null;

            var weeklyStats = await _repository.AggregateWeeklyStatsAsync(ct);

            _messenger.Send(new UsageDataFetchedMessage(inserted, latestPeriod, latestUser, latestSub, aggStats, weeklyStats, nowMs));

            _logger.LogInformation("同步完成：拉取 {Total} 条事件，入库 {Inserted} 条", allEvents.Count, inserted);

            // 每次同步成功都推送通知（含启动首次拉取）
            await NotifySyncSuccessAsync(inserted, aggStats, weeklyStats, ct);
        }
        catch (CursorApiAuthException ex)
        {
            _logger.LogError(ex, "认证失败");
            _messenger.Send(new SyncFailedMessage(ex.Message));
        }
        catch (CursorApiException ex)
        {
            _logger.LogError(ex, "API 调用失败");
            _messenger.Send(new SyncFailedMessage(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步过程发生未预期异常");
            _messenger.Send(new SyncFailedMessage($"未预期错误：{ex.Message}"));
        }
    }

    /// <summary>
    /// 同步成功后推送通知，包含本次新增事件数和本周期/本周用量摘要。
    /// </summary>
    private async Task NotifySyncSuccessAsync(
        int newEventsCount,
        UsageAggregateStats? periodStats,
        UsageAggregateStats? weeklyStats,
        CancellationToken ct)
    {
        try
        {
            var periodSpend = periodStats?.TotalSpendCents / 100m ?? 0m;
            var weekSpend = weeklyStats?.TotalSpendCents / 100m ?? 0m;
            var periodTokens = periodStats?.TotalTokens ?? 0;
            var weekTokens = weeklyStats?.TotalTokens ?? 0;

            var title = "Cursor 用量更新";
            var body = $"新增 {newEventsCount} 条事件\n"
                       + $"本周期：{periodTokens:N0} tokens，${periodSpend:F2}\n"
                       + $"本周：{weekTokens:N0} tokens，${weekSpend:F2}";

            await _notification.ShowAsync(title, body, ct);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _options.LastNotificationTimeMs = nowMs;
            _messenger.Send(new NotificationShownMessage(nowMs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步成功通知发送失败");
        }
    }

    private async Task SafeNotifyAsync(CancellationToken ct)
    {
        try
        {
            var period = await _repository.GetLatestPeriodUsageAsync(ct);
            if (period is null)
            {
                return;
            }

            var usedTokens = period.UsedTokens;
            var totalSpend = period.TotalSpendCents / 100m;
            var title = "Cursor 用量提醒";
            var body = $"本周期已用 token：{usedTokens:N0}\n本周期支出：${totalSpend:F2}";

            await _notification.ShowAsync(title, body, ct);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _options.LastNotificationTimeMs = nowMs;
            _messenger.Send(new NotificationShownMessage(nowMs));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知发送失败");
        }
    }

    private static UsageEventEntity MapToEntity(CursorUsageEventDto dto)
    {
        var ts = dto.TryGetTimestampMs();
        return new UsageEventEntity
        {
            Timestamp = ts,
            UserEmail = dto.UserEmail ?? string.Empty,
            Model = dto.Model,
            Kind = dto.Kind,
            MaxMode = dto.MaxMode,
            RequestsCosts = dto.RequestsCosts,
            IsTokenBasedCall = dto.IsTokenBasedCall,
            IsChargeable = dto.IsChargeable,
            IsHeadless = dto.IsHeadless,
            InputTokens = dto.TokenUsage?.InputTokens ?? 0,
            OutputTokens = dto.TokenUsage?.OutputTokens ?? 0,
            CacheReadTokens = dto.TokenUsage?.CacheReadTokens ?? 0,
            CacheWriteTokens = dto.TokenUsage?.CacheWriteTokens ?? 0,
            TotalCents = dto.TokenUsage?.TotalCents ?? 0,
            CursorTokenFee = dto.CursorTokenFee,
            ChargedCents = dto.ChargedCents,
            FetchTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static PeriodUsageEntity MapToPeriodEntity(CursorPeriodUsageDto dto, long fetchMs, string? planName)
    {
        return new PeriodUsageEntity
        {
            FetchTime = fetchMs,
            PeriodStart = dto.PeriodStartMs,
            PeriodEnd = dto.PeriodEndMs,
            // planName 来自 stripe.membershipType（period API 自身不含计划名）
            PlanName = planName,
            // 以下字段 period-usage API 不直接提供，由 events 聚合或留 0
            IncludedRequests = 0,
            UsedRequests = 0,
            UsedTokens = 0,
            TotalSpendCents = dto.TotalSpendCents,
            FastPremiumRequests = 0,
            // remaining 为剩余额度（美分），复用此字段展示
            RemainingRequests = dto.RemainingCents,
            RawJson = JsonSerializer.Serialize(dto)
        };
    }

    /// <summary>
    /// 将多个 GET API 响应合并映射为订阅实体。
    /// 数据源优先级：billing-cycle（周期）> period-usage（周期/支出备用）> stripe（计划/状态）> invoices（发票数/起始）> profile（用户标识/账号创建）。
    /// </summary>
    private static SubscriptionEntity MapToSubscriptionEntity(
        CursorStripeSubscriptionDto? stripe,
        CursorBillingCycleDto? cycle,
        CursorInvoicesDto? invoices,
        CursorUserProfileDto? profile,
        CursorPeriodUsageDto? period,
        long fetchMs)
    {
        // 当前周期：优先 billing-cycle，其次 period-usage
        var periodStart = cycle?.StartMs ?? period?.PeriodStartMs ?? 0;
        var periodEnd = cycle?.EndMs ?? period?.PeriodEndMs ?? 0;

        // 订阅起始：取最早发票日期；无发票则用账号创建时间
        var subStart = 0L;
        if (invoices?.Invoices is { Count: > 0 })
        {
            var earliest = invoices.Invoices
                .Where(i => i.DateMs > 0)
                .MinBy(i => i.DateMs);
            if (earliest?.DateMs > 0)
                subStart = earliest.DateMs;
        }
        if (subStart == 0 && !string.IsNullOrEmpty(profile?.Profile?.CreatedAt)
            && DateTimeOffset.TryParse(profile.Profile.CreatedAt, out var created))
        {
            subStart = created.ToUnixTimeMilliseconds();
        }

        // 是否在周期结束时取消：stripe.pendingCancellationDate 非空表示已申请取消
        var cancelAtPeriodEnd = !string.IsNullOrEmpty(stripe?.PendingCancellationDate);

        return new SubscriptionEntity
        {
            SnapshotTime = fetchMs,
            Plan = stripe?.MembershipType,
            Status = stripe?.SubscriptionStatus,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodEnd,
            TrialEnd = 0, // stripe 无试用结束时间字段（仅 TrialEligible/TrialLengthDays）
            CancelAtPeriodEnd = cancelAtPeriodEnd,
            SubscriptionStart = subStart,
            InvoiceCount = invoices?.Invoices?.Count ?? invoices?.Total ?? 0,
            // 无 email API，用 profile.handle 作为用户标识
            Email = profile?.Profile?.Handle,
            RawJson = JsonSerializer.Serialize(new
            {
                stripe,
                cycle,
                invoices,
                profile,
                period
            })
        };
    }

    /// <summary>
    /// 检测会话过期时间，临近过期（7 天内）或已过期时通知 UI 提醒用户更新 cookie。
    /// </summary>
    private void CheckSessionExpiry(CursorSessionsDto? sessions)
    {
        if (sessions?.LatestWebSessionExpiryUtc is not { } expiryUtc)
        {
            return;
        }

        var remaining = expiryUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _logger.LogWarning("Cursor session 已过期（ expiry {Expiry}），请更新 cookie", expiryUtc);
            _messenger.Send(new CookieExpiringSoonMessage(expiryUtc, IsExpired: true));
        }
        else if (remaining <= TimeSpan.FromDays(7))
        {
            _logger.LogWarning("Cursor session 将在 {Days} 天后过期（{Expiry}）", (int)remaining.TotalDays, expiryUtc);
            _messenger.Send(new CookieExpiringSoonMessage(expiryUtc, IsExpired: false));
        }
    }

    /// <summary>
    /// 从 secrets.dat 加载已保存的 token 到 _options.SessionToken。
    /// 确保 sync 服务启动时 token 已可用，不依赖 ViewModel 的生命周期。
    /// </summary>
    private async Task LoadSavedTokenAsync()
    {
        try
        {
            if (!File.Exists(_settings.SecretsPath))
                return;

            var cipher = await File.ReadAllBytesAsync(_settings.SecretsPath);
            var plain = _protector.Decrypt(cipher);
            if (!string.IsNullOrEmpty(plain))
            {
                _options.SessionToken = plain;
                _logger.LogInformation("已从 secrets.dat 加载 session token");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从 secrets.dat 加载 token 失败（不影响后续手动输入）");
        }
    }
}
