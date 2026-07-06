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

            // 拉取计费周期汇总
            CursorPeriodUsageDto? periodDto = null;
            try
            {
                periodDto = await _apiClient.GetCurrentPeriodUsageAsync(_options.SessionToken, ct);
                if (periodDto is not null)
                {
                    await _repository.UpsertPeriodUsageAsync(MapToPeriodEntity(periodDto, nowMs), ct);
                }
            }
            catch (CursorApiException ex)
            {
                _logger.LogWarning(ex, "拉取计费周期汇总失败（不影响事件入库）");
            }

            // 拉取订阅/账单信息（尝试 JSON API 端点，静默跳过失败）
            SubscriptionEntity? sub = null;
            try
            {
                var billingData = await _apiClient.GetBillingPageDataAsync(_options.SessionToken, ct);
                if (billingData?.Props?.PageProps is { } pageProps)
                {
                    var subEntity = MapToSubscriptionEntity(pageProps, nowMs);
                    await _repository.UpsertSubscriptionAsync(subEntity, ct);
                    sub = subEntity;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "拉取账单页面数据失败（不影响事件入库）");
            }

            // 用户信息（多数据源：事件表邮箱 > periodDto > billing 页面）
            var userEmail = allEvents.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.UserEmail))?.UserEmail
                            ?? periodDto?.Email
                            ?? sub?.Email;

            var planName = periodDto?.PlanName ?? sub?.Plan;
            if (userEmail is not null)
            {
                await _repository.UpsertUserInfoAsync(new UserInfoEntity
                {
                    Email = userEmail,
                    PlanName = planName,
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

            _messenger.Send(new UsageDataFetchedMessage(inserted, latestPeriod, latestUser, latestSub, aggStats, nowMs));

            _logger.LogInformation("同步完成：拉取 {Total} 条事件，入库 {Inserted} 条", allEvents.Count, inserted);
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

    private static PeriodUsageEntity MapToPeriodEntity(CursorPeriodUsageDto dto, long fetchMs)
    {
        return new PeriodUsageEntity
        {
            FetchTime = fetchMs,
            PeriodStart = dto.PeriodStart ?? 0,
            PeriodEnd = dto.PeriodEnd ?? 0,
            PlanName = dto.PlanName,
            IncludedRequests = dto.IncludedRequests ?? 0,
            UsedRequests = dto.UsedRequests ?? 0,
            UsedTokens = dto.UsedTokens ?? 0,
            TotalSpendCents = dto.TotalSpendCents ?? 0,
            FastPremiumRequests = dto.FastPremiumRequests ?? 0,
            RemainingRequests = dto.RemainingRequests ?? 0,
            RawJson = JsonSerializer.Serialize(dto)
        };
    }

    private static SubscriptionEntity MapToSubscriptionEntity(CursorBillingPagePropsData pageProps, long fetchMs)
    {
        var sub = pageProps.Subscription;
        var invoices = pageProps.Invoices;

        // 从最早发票推断订阅起始日期
        var subStart = sub?.CurrentPeriodStart ?? 0;
        if (invoices?.Count > 0)
        {
            var earliestInvoice = invoices
                .Where(i => i.PeriodStart.HasValue)
                .MinBy(i => i.PeriodStart);
            if (earliestInvoice?.PeriodStart > 0)
                subStart = Math.Min(subStart, earliestInvoice.PeriodStart.Value);
        }

        return new SubscriptionEntity
        {
            SnapshotTime = fetchMs,
            Plan = sub?.Plan,
            Status = sub?.Status,
            CurrentPeriodStart = sub?.CurrentPeriodStart ?? 0,
            CurrentPeriodEnd = sub?.CurrentPeriodEnd ?? 0,
            TrialEnd = sub?.TrialEnd ?? 0,
            CancelAtPeriodEnd = sub?.CancelAtPeriodEnd ?? false,
            SubscriptionStart = subStart,
            InvoiceCount = invoices?.Count ?? 0,
            Email = pageProps.Team?.Email,
            RawJson = JsonSerializer.Serialize(pageProps)
        };
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
