using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Larpx.PersonalTools.CursorUsageNotify.Services.Notifications;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling
{
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
        private readonly SecureTokenHolder _tokenHolder;
        private readonly AppSettings _settings;
        private readonly ILogger<UsageSyncHostedService> _logger;

        // 单次同步总超时
        private static readonly TimeSpan SyncTimeout = TimeSpan.FromMinutes(5);
        // 辅助 API 批量并发超时
        private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(60);
        // 单次同步事件条数上限，防止内存膨胀
        private const int MaxEventsPerSync = 50000;
        // 分页安全上限
        private const int MaxPages = 50;
        // 连续失败上限，超过后暂停同步
        private const int MaxConsecutiveFailures = 10;

        // RawJson 序列化专用 options：显式启用反射 TypeInfoResolver，
        // 避免 .NET 10 在 publish 配置下默认 source-gen resolver 无法处理
        // ExtraFields（Dictionary<string, object>）反序列化后产生的 JsonElement。
        private static readonly JsonSerializerOptions RawJsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        /// <summary>
        /// 初始化 <see cref="UsageSyncHostedService"/> 实例并注入所需依赖。
        /// </summary>
        public UsageSyncHostedService(
            ICursorApiClient apiClient,
            IUsageRepository repository,
            INotificationService notification,
            IMessenger messenger,
            UsageSyncOptions options,
            TokenProtector protector,
            SecureTokenHolder tokenHolder,
            AppSettings settings,
            ILogger<UsageSyncHostedService> logger)
        {
            _apiClient = apiClient;
            _repository = repository;
            _notification = notification;
            _messenger = messenger;
            _options = options;
            _protector = protector;
            _tokenHolder = tokenHolder;
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
                // 失败退避：连续失败次数越多间隔越长，封顶 24 小时
                var interval = GetBackoffIntervalMinutes();
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(interval), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_options.IsRunning && _tokenHolder.HasToken)
                {
                    await SafeSyncAsync(ct);

                    // 连续失败上限：暂停同步并通知用户
                    if (_options.ConsecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _options.IsRunning = false;
                        _messenger.Send(new ToggleSyncMessage(false));
                        _messenger.Send(new SyncFailedMessage("连续失败已达上限，已暂停同步，请检查网络或更新 token"));
                        _logger.LogWarning("连续失败 {Count} 次，已暂停同步", _options.ConsecutiveFailures);
                    }
                }
            }
        }

        /// <summary>
        /// 根据基础间隔与连续失败次数计算退避间隔（指数增长，封顶 24 小时）。
        /// </summary>
        private int GetBackoffIntervalMinutes()
        {
            var baseInterval = Math.Max(1, _options.SyncIntervalMinutes);
            var failures = _options.ConsecutiveFailures;
            if (failures <= 0)
            {
                return baseInterval;
            }

            // 倍数上限 8，避免指数膨胀过快
            var multiplier = (int)Math.Min(Math.Pow(2, failures), 8);
            var interval = baseInterval * multiplier;
            return Math.Min(interval, 24 * 60);
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
            if (!_tokenHolder.HasToken)
            {
                _logger.LogWarning("跳过同步：session token 为空");
                return;
            }

            // 通知 UI 同步开始（显示进度条，避免误以为卡顿）
            _messenger.Send(new SyncStartedMessage());

            // 单次同步超时保护：5 分钟，超时中止本次但不影响后续循环
            using var syncCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            syncCts.CancelAfter(SyncTimeout);
            var syncToken = syncCts.Token;

            try
            {
                await _tokenHolder.UseAsync((token, _) => SyncCoreAsync(token, syncToken), syncToken);
                _options.ConsecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (syncCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("单次同步超时（{Minutes} 分钟），中止本次", SyncTimeout.TotalMinutes);
                _options.ConsecutiveFailures++;
            }
            catch (CursorApiAuthException ex)
            {
                _logger.LogError(ex, "认证失败");
                ClearToken();
                _messenger.Send(new SyncFailedMessage(ex.Message));
                _options.ConsecutiveFailures++;
            }
            catch (CursorApiEndpointChangedException ex)
            {
                _logger.LogError(ex, "端点已变更：{Path}", ex.EndpointPath);
                _messenger.Send(new SyncFailedMessage($"接口已变更：{ex.EndpointPath}，请检查客户端是否需要更新"));
                _options.ConsecutiveFailures++;
            }
            catch (CursorApiException ex)
            {
                _logger.LogError(ex, "API 调用失败");
                _messenger.Send(new SyncFailedMessage(ex.Message));
                _options.ConsecutiveFailures++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步过程发生未预期异常");
                _messenger.Send(new SyncFailedMessage($"未预期错误：{ex.Message}"));
                _options.ConsecutiveFailures++;
            }
        }

        /// <summary>
        /// 同步核心逻辑：借出明文 token 执行所有 API 拉取与入库。
        /// 辅助 6 个 GET 并发执行（Task.WhenAll），总超时 60 秒。
        /// </summary>
        private async Task<int> SyncCoreAsync(string token, CancellationToken ct)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startTs = _options.LastSyncTimeMs > 0 ? _options.LastSyncTimeMs : 0;

            var allEvents = new List<CursorUsageEventDto>();
            var page = 1;
            const int pageSize = 500;
            int total;

            // usage-events 分页是主体，必须先完成
            do
            {
                var resp = await _apiClient.GetFilteredUsageEventsAsync(token, page, pageSize, startTs, ct);
                allEvents.AddRange(resp.UsageEventsDisplay);
                total = resp.TotalUsageEventsCount;
                page++;
            } while (allEvents.Count < total && page <= MaxPages && allEvents.Count < MaxEventsPerSync);

            if (allEvents.Count >= MaxEventsPerSync)
            {
                _logger.LogWarning("已达单次同步事件上限 {Max}，本次拉取提前结束", MaxEventsPerSync);
            }

            var entities = allEvents.Select(MapToEntity).ToList();
            var inserted = await _repository.UpsertUsageEventsAsync(entities, ct);

            // 并发拉取 6 个辅助 GET：period / stripe / profile / cycle / invoices / sessions
            using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            batchCts.CancelAfter(BatchTimeout);
            var batchToken = batchCts.Token;

            var periodTask = SafeGetAsync(() => _apiClient.GetCurrentPeriodUsageAsync(token, batchToken));
            var stripeTask = SafeGetAsync(() => _apiClient.GetStripeSubscriptionAsync(token, batchToken));
            var profileTask = SafeGetAsync(() => _apiClient.GetUserProfileAsync(token, batchToken));
            var cycleTask = SafeGetAsync(() => _apiClient.GetBillingCycleAsync(token, batchToken));
            var invoicesTask = SafeGetAsync(() => _apiClient.ListInvoicesAsync(token, batchToken));
            var sessionsTask = SafeGetAsync(() => _apiClient.GetSessionsAsync(token, batchToken));

            try
            {
                await Task.WhenAll(periodTask, stripeTask, profileTask, cycleTask, invoicesTask, sessionsTask);
            }
            catch (CursorApiAuthException)
            {
                throw; // 认证失败向上抛，触发 token 失效处理
            }
            catch (CursorApiEndpointChangedException)
            {
                throw; // 端点变更向上抛，由 SafeSyncAsync 明确通知用户
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "辅助 API 批量拉取部分失败（不影响事件入库）");
            }

            // 各任务在 SafeGetAsync 内已将非认证异常转为 null，可安全 await
            CursorPeriodUsageDto? periodDto = await periodTask;
            CursorStripeSubscriptionDto? stripeDto = await stripeTask;
            CursorUserProfileDto? profileDto = await profileTask;
            CursorBillingCycleDto? cycleDto = await cycleTask;
            CursorInvoicesDto? invoicesDto = await invoicesTask;
            CursorSessionsDto? sessionsDto = await sessionsTask;

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

            return inserted;
        }

        /// <summary>
        /// 包裹单个辅助 GET：将认证异常外的所有失败转为 null，
        /// 避免 Task.WhenAll 因单个 faulted 而抛出（认证异常仍向上抛）。
        /// </summary>
        private static async Task<T?> SafeGetAsync<T>(Func<Task<T>> factory) where T : class
        {
            try
            {
                return await factory();
            }
            catch (CursorApiAuthException)
            {
                throw;
            }
            catch (CursorApiEndpointChangedException)
            {
                throw;
            }
            catch
            {
                // 包含 OperationCanceledException（批超时）、网络错误、反序列化失败等
                return null;
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
                var mode = _options.TokenDisplayMode;

                var title = "Cursor 用量更新";
                var body = $"新增 {newEventsCount} 条事件\n"
                           + $"本周期：{TokenFormatter.Format(periodTokens, mode)} tokens，${periodSpend:F2}\n"
                           + $"本周：{TokenFormatter.Format(weekTokens, mode)} tokens，${weekSpend:F2}";

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
                RawJson = SafeSerialize(dto)
            };
        }

        /// <summary>
        /// 安全序列化 DTO 为 JSON 字符串：使用显式启用反射的 RawJsonOptions，
        /// 失败时记录日志并返回 null，避免 RawJson 字段问题阻断同步主流程。
        /// </summary>
        /// <typeparam name="T">待序列化的类型。</typeparam>
        /// <param name="value">待序列化的实例。</param>
        /// <returns>JSON 字符串；序列化失败时返回 null。</returns>
        private static string? SafeSerialize<T>(T value)
        {
            try
            {
                return JsonSerializer.Serialize(value, RawJsonOptions);
            }
            catch (Exception ex)
            {
                // RawJson 仅用于诊断扩展字段，序列化失败不应阻断入库；用 Serilog 静态 logger 记录
                Log.Logger.Warning(ex, "RawJson 序列化失败（类型：{Type}）", typeof(T).Name);
                return null;
            }
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
                RawJson = SafeSerialize(new
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
        /// 已过期时主动清除内存与磁盘 token，避免无效敏感数据残留。
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
                _logger.LogWarning("Cursor session 已过期（expiry {Expiry}），清除本地 token", expiryUtc);
                ClearToken();
                _messenger.Send(new CookieExpiringSoonMessage(expiryUtc, IsExpired: true));
            }
            else if (remaining <= TimeSpan.FromDays(7))
            {
                _logger.LogWarning("Cursor session 将在 {Days} 天后过期（{Expiry}）", (int)remaining.TotalDays, expiryUtc);
                _messenger.Send(new CookieExpiringSoonMessage(expiryUtc, IsExpired: false));
            }
        }

        /// <summary>
        /// 清除内存中的 token 并删除磁盘 secrets.dat，通知 UI 刷新为"未设置"状态。
        /// 在认证失败或检测到过期时调用。
        /// </summary>
        private void ClearToken()
        {
            _tokenHolder.Clear();
            _messenger.Send(new TokenStateChangedMessage(false));
            try
            {
                if (File.Exists(_settings.SecretsPath))
                {
                    File.Delete(_settings.SecretsPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除 secrets.dat 失败");
            }
        }

        /// <summary>
        /// 从 secrets.dat 加载已保存的 token 到 SecureTokenHolder。
        /// 全程以 byte[] 形式传递，避免明文 string 驻留。
        /// </summary>
        private async Task LoadSavedTokenAsync()
        {
            try
            {
                if (!File.Exists(_settings.SecretsPath))
                    return;

                var cipher = await File.ReadAllBytesAsync(_settings.SecretsPath);
                var plain = _protector.DecryptToBytes(cipher);
                if (plain.Length > 0)
                {
                    _tokenHolder.SetBytes(plain);
                    _logger.LogInformation("已从 secrets.dat 加载 session token");
                    _messenger.Send(new TokenStateChangedMessage(true));
                }
                // 立即清除解密后的明文副本（SetBytes 内部已克隆）
                Array.Clear(plain);
            }
            catch (InvalidOperationException ex)
            {
                // DPAPI 不可用：记录致命日志，token 无法加载但程序可继续运行（用户可手动输入）
                _logger.LogCritical(ex, "DPAPI 不可用，无法加载已保存的 token");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "从 secrets.dat 加载 token 失败（不影响后续手动输入）");
            }
        }
    }
}
