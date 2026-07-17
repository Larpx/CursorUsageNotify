using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Larpx.PersonalTools.CursorUsageNotify.Services.Notifications;
using Larpx.PersonalTools.CursorUsageNotify.Services.Platforms;
using Larpx.PersonalTools.CursorUsageNotify.Services.Security;
using Larpx.PersonalTools.CursorUsageNotify.Services.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling
{
    /// <summary>
    /// 后台定时同步服务：遍历所有启用的 <see cref="IPlatformProvider"/> 执行数据采集，
    /// 统一入库、聚合统计并触发 Toast 通知。
    /// 通过 PeriodicTimer 控制两个独立循环（拉取 + 通知），避免引入 Quartz 复杂度。
    /// </summary>
    public sealed class UsageSyncHostedService : BackgroundService
    {
        private readonly IReadOnlyList<IPlatformProvider> _providers;
        private readonly IUsageRepository _repository;
        private readonly INotificationService _notification;
        private readonly IMessenger _messenger;
        private readonly UsageSyncOptions _options;
        private readonly TokenProtector _protector;
        private readonly SecureTokenHolder _tokenHolder;
        private readonly AppSettings _settings;
        private readonly UserPreferences _userPrefs;
        private readonly ILogger<UsageSyncHostedService> _logger;

        // 单次同步总超时
        private static readonly TimeSpan SyncTimeout = TimeSpan.FromMinutes(5);
        // 连续失败上限，超过后暂停同步
        private const int MaxConsecutiveFailures = 10;

        /// <summary>
        /// 初始化 <see cref="UsageSyncHostedService"/> 实例并注入所需依赖。
        /// </summary>
        public UsageSyncHostedService(
            IEnumerable<IPlatformProvider> providers,
            IUsageRepository repository,
            INotificationService notification,
            IMessenger messenger,
            UsageSyncOptions options,
            TokenProtector protector,
            SecureTokenHolder tokenHolder,
            AppSettings settings,
            UserPreferences userPrefs,
            ILogger<UsageSyncHostedService> logger)
        {
            _providers = providers.ToList();
            _repository = repository;
            _notification = notification;
            _messenger = messenger;
            _options = options;
            _protector = protector;
            _tokenHolder = tokenHolder;
            _settings = settings;
            _userPrefs = userPrefs;
            _logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("用量同步服务已启动（注册 {Count} 个平台 Provider）", _providers.Count);

            // 启动时从 secrets 加载已保存的 token（所有平台）
            await LoadSavedTokensAsync();

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

                if (_options.IsRunning && _tokenHolder.HasAnyToken)
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

        /// <summary>
        /// 遍历所有启用的平台执行同步。单平台失败不影响其他平台。
        /// </summary>
        private async Task SafeSyncAsync(CancellationToken ct)
        {
            if (!_tokenHolder.HasAnyToken)
            {
                _logger.LogWarning("跳过同步：所有平台 token 为空");
                return;
            }

            // 通知 UI 同步开始（显示进度条，避免误以为卡顿）
            _messenger.Send(new SyncStartedMessage());

            // 单次同步超时保护：5 分钟，超时中止本次但不影响后续循环
            using var syncCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            syncCts.CancelAfter(SyncTimeout);
            var syncToken = syncCts.Token;

            var anySuccess = false;
            var hadAnyPlatform = false;

            foreach (var provider in _providers)
            {
                // 未启用的平台跳过（不影响历史数据）
                if (!_userPrefs.IsPlatformEnabled(provider.Platform))
                {
                    continue;
                }

                // 无 token 的平台跳过
                if (!_tokenHolder.HasToken(provider.Platform))
                {
                    continue;
                }

                hadAnyPlatform = true;

                try
                {
                    await _tokenHolder.UseAsync(
                        provider.Platform,
                        (token, _) => SyncPlatformCoreAsync(provider, token, syncToken),
                        syncToken);
                    anySuccess = true;
                }
                catch (OperationCanceledException) when (syncCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("单次同步超时（{Minutes} 分钟），中止本次", SyncTimeout.TotalMinutes);
                    _options.ConsecutiveFailures++;
                    break; // 超时后中止整个同步循环
                }
                catch (PlatformApiException ex) when (ex.IsAuthError)
                {
                    _logger.LogError(ex, "{Platform} 认证失败", ex.Platform);
                    ClearPlatformToken(ex.Platform);
                    _messenger.Send(new SyncFailedMessage(ex.Message, ex.Platform));
                    _options.ConsecutiveFailures++;
                }
                catch (PlatformApiException ex) when (ex.IsEndpointChanged)
                {
                    _logger.LogError(ex, "{Platform} 端点已变更", ex.Platform);
                    _messenger.Send(new SyncFailedMessage($"接口已变更：{ex.Message}", ex.Platform));
                    _options.ConsecutiveFailures++;
                }
                catch (PlatformApiException ex)
                {
                    _logger.LogError(ex, "{Platform} API 调用失败", ex.Platform);
                    _messenger.Send(new SyncFailedMessage(ex.Message, ex.Platform));
                    _options.ConsecutiveFailures++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Platform} 同步过程发生未预期异常", provider.Platform);
                    _messenger.Send(new SyncFailedMessage($"未预期错误：{ex.Message}", provider.Platform));
                    _options.ConsecutiveFailures++;
                }
            }

            if (hadAnyPlatform && anySuccess)
            {
                _options.ConsecutiveFailures = 0;
            }
        }

        /// <summary>
        /// 单平台同步核心逻辑：调用 Provider.SyncAsync 获取数据，统一入库并聚合统计。
        /// </summary>
        private async Task<int> SyncPlatformCoreAsync(IPlatformProvider provider, string token, CancellationToken ct)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = await provider.SyncAsync(token, ct);

            // 入库（统一处理所有平台的实体）
            var inserted = 0;
            if (result.UsageEvents.Count > 0)
            {
                inserted = await _repository.UpsertUsageEventsAsync(result.UsageEvents.ToList(), ct);
            }
            if (result.PeriodUsage is not null)
            {
                await _repository.UpsertPeriodUsageAsync(result.PeriodUsage, ct);
            }
            if (result.UserInfo is not null)
            {
                await _repository.UpsertUserInfoAsync(result.UserInfo, ct);
            }
            if (result.Subscription is not null)
            {
                await _repository.UpsertSubscriptionAsync(result.Subscription, ct);
            }

            // 会话过期检测（仅 Cursor 有 SessionExpiryUtc）
            if (result.SessionExpiryUtc is { } expiryUtc)
            {
                CheckSessionExpiry(provider.Platform, expiryUtc);
            }

            // 只有 Cursor 平台使用 LastSyncTimeMs 做增量同步
            // DeepSeek 每次拉取「往前两个自然月」全量窗口，不需要增量时间戳
            if (provider.Platform == PlatformType.Cursor)
            {
                _options.LastSyncTimeMs = nowMs;
            }

            // 聚合统计与消息广播
            var latestPeriod = await _repository.GetLatestPeriodUsageAsync(provider.Platform, ct);
            var latestUser = await _repository.GetLatestUserInfoAsync(provider.Platform, ct);
            var latestSub = await _repository.GetLatestSubscriptionAsync(provider.Platform, ct);

            // 按订阅周期从 events 表聚合统计
            var aggPeriodStart = latestSub?.CurrentPeriodStart
                                 ?? latestPeriod?.PeriodStart
                                 ?? 0;
            var aggPeriodEnd = latestSub?.CurrentPeriodEnd
                               ?? latestPeriod?.PeriodEnd
                               ?? 0;

            // DeepSeek 可按设置过滤单个 API Key；Cursor 不过滤
            var apiKeyFilter = provider.Platform == PlatformType.DeepSeek
                ? _userPrefs.GetDeepSeekApiKeyFilter()
                : null;

            var aggStats = aggPeriodStart > 0 && aggPeriodEnd > 0
                ? await _repository.AggregateStatsAsync(aggPeriodStart, aggPeriodEnd, provider.Platform, apiKeyFilter, ct)
                : null;

            var weeklyStats = await _repository.AggregateWeeklyStatsAsync(provider.Platform, apiKeyFilter, ct);

            _messenger.Send(new UsageDataFetchedMessage(
                inserted, latestPeriod, latestUser, latestSub, aggStats, weeklyStats, nowMs, provider.Platform));

            _logger.LogInformation("{Platform} 同步完成：{Summary}", provider.Platform, result.SyncSummary ?? "完成");

            // 每次同步成功都推送通知（含启动首次拉取）
            await NotifySyncSuccessAsync(provider.Platform, inserted, aggStats, weeklyStats, ct);

            return inserted;
        }

        /// <summary>
        /// 同步成功后推送通知，包含本次新增事件数和本周期/本周用量摘要。
        /// 受设置页「平台通知开关」控制：关闭的平台不推送。
        /// </summary>
        private async Task NotifySyncSuccessAsync(
            PlatformType platform,
            int newEventsCount,
            UsageAggregateStats? periodStats,
            UsageAggregateStats? weeklyStats,
            CancellationToken ct)
        {
            if (!_userPrefs.IsNotificationEnabled(platform))
            {
                return;
            }

            try
            {
                var periodSpend = periodStats?.TotalSpendCents / 100m ?? 0m;
                var weekSpend = weeklyStats?.TotalSpendCents / 100m ?? 0m;
                var periodTokens = periodStats?.TotalTokens ?? 0;
                var weekTokens = weeklyStats?.TotalTokens ?? 0;
                var mode = _options.TokenDisplayMode;
                var currency = GetCurrencySymbol(platform);
                var periodLabel = platform == PlatformType.DeepSeek ? "本月" : "本周期";

                var title = $"{platform} 用量更新";
                var body = $"新增 {newEventsCount} 条事件\n"
                           + $"{periodLabel}：{TokenFormatter.Format(periodTokens, mode)} tokens，{currency}{periodSpend:F2}\n"
                           + $"本周：{TokenFormatter.Format(weekTokens, mode)} tokens，{currency}{weekSpend:F2}";

                // DeepSeek 补充请求次数（period.UsedRequests / AggregateStats.TotalRequests）
                if (platform == PlatformType.DeepSeek && periodStats is not null)
                {
                    body += $"\n请求：{periodStats.TotalRequests:N0} 次";
                }

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
                // 两边通知都关：整次定时提醒跳过
                if (!_userPrefs.HasAnyNotificationEnabled())
                {
                    return;
                }

                // 仅汇总「启用平台 + 开启通知 + 有 token」的用量
                var lines = new List<string>();

                foreach (var provider in _providers)
                {
                    if (!_userPrefs.IsPlatformEnabled(provider.Platform))
                        continue;
                    if (!_userPrefs.IsNotificationEnabled(provider.Platform))
                        continue;
                    if (!_tokenHolder.HasToken(provider.Platform))
                        continue;

                    var period = await _repository.GetLatestPeriodUsageAsync(provider.Platform, ct);
                    if (period is null)
                        continue;

                    var currency = GetCurrencySymbol(provider.Platform);
                    var totalSpend = period.TotalSpendCents / 100m;
                    if (provider.Platform == PlatformType.DeepSeek)
                    {
                        lines.Add(
                            $"DeepSeek：{period.UsedTokens:N0} tokens，{currency}{totalSpend:F2}，{period.UsedRequests:N0} 次请求");
                    }
                    else
                    {
                        lines.Add($"{provider.Platform}：{period.UsedTokens:N0} tokens，{currency}{totalSpend:F2}");
                    }
                }

                if (lines.Count == 0)
                    return;

                var title = "用量提醒";
                var body = string.Join("\n", lines);

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

        /// <summary>
        /// 获取平台对应的货币符号：Cursor 按美元计费（$），DeepSeek 按人民币计费（¥）。
        /// </summary>
        private static string GetCurrencySymbol(PlatformType platform) => platform switch
        {
            PlatformType.Cursor => "$",
            PlatformType.DeepSeek => "¥",
            _ => "$"
        };

        /// <summary>
        /// 检测会话过期时间，临近过期（7 天内）或已过期时通知 UI 提醒用户更新。
        /// 已过期时主动清除内存与磁盘 token，避免无效敏感数据残留。
        /// </summary>
        private void CheckSessionExpiry(PlatformType platform, DateTime expiryUtc)
        {
            var remaining = expiryUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _logger.LogWarning("{Platform} session 已过期（expiry {Expiry}），清除本地 token", platform, expiryUtc);
                ClearPlatformToken(platform);
                _messenger.Send(new CookieExpiringSoonMessage(expiryUtc, IsExpired: true, platform));
            }
            else if (remaining <= TimeSpan.FromDays(7))
            {
                _logger.LogWarning("{Platform} session 将在 {Days} 天后过期（{Expiry}）", platform, (int)remaining.TotalDays, expiryUtc);
                _messenger.Send(new CookieExpiringSoonMessage(expiryUtc, IsExpired: false, platform));
            }
        }

        /// <summary>
        /// 获取指定平台的加密 token 持久化文件路径。
        /// Cursor 保持原 secrets.dat 路径兼容旧版；其他平台使用 secrets_{platform}.dat。
        /// </summary>
        private string GetSecretsPath(PlatformType platform) => platform switch
        {
            PlatformType.Cursor => _settings.SecretsPath,
            PlatformType.DeepSeek => Path.Combine(
                Path.GetDirectoryName(_settings.SecretsPath) ?? string.Empty,
                "secrets_deepseek.dat"),
            _ => _settings.SecretsPath
        };

        /// <summary>
        /// 清除指定平台的内存 token 并删除磁盘 secrets 文件，通知 UI 刷新为"未设置"状态。
        /// 在认证失败或检测到过期时调用。
        /// </summary>
        private void ClearPlatformToken(PlatformType platform)
        {
            _tokenHolder.Clear(platform);
            _messenger.Send(new TokenStateChangedMessage(false, platform));
            try
            {
                var path = GetSecretsPath(platform);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除 {Platform} secrets 文件失败", platform);
            }
        }

        /// <summary>
        /// 从各平台的 secrets 文件加载已保存的 token 到 SecureTokenHolder。
        /// 全程以 byte[] 形式传递，避免明文 string 驻留。
        /// </summary>
        private async Task LoadSavedTokensAsync()
        {
            foreach (var platform in new[] { PlatformType.Cursor, PlatformType.DeepSeek })
            {
                await LoadSavedTokenForPlatformAsync(platform);
            }
        }

        /// <summary>
        /// 加载指定平台的 token。
        /// </summary>
        private async Task LoadSavedTokenForPlatformAsync(PlatformType platform)
        {
            try
            {
                var path = GetSecretsPath(platform);
                if (!File.Exists(path))
                    return;

                var cipher = await File.ReadAllBytesAsync(path);
                var plain = _protector.DecryptToBytes(cipher);
                if (plain.Length > 0)
                {
                    _tokenHolder.SetBytes(platform, plain);
                    _logger.LogInformation("已从 {Path} 加载 {Platform} token", path, platform);
                    _messenger.Send(new TokenStateChangedMessage(true, platform));
                }
                // 立即清除解密后的明文副本（SetBytes 内部已克隆）
                Array.Clear(plain);
            }
            catch (InvalidOperationException ex)
            {
                // DPAPI 不可用：记录致命日志，token 无法加载但程序可继续运行
                _logger.LogCritical(ex, "DPAPI 不可用，无法加载 {Platform} token", platform);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "从 secrets 文件加载 {Platform} token 失败", platform);
            }
        }
    }
}
