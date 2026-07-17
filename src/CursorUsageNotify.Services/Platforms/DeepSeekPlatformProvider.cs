using System.Text.Json;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Microsoft.Extensions.Logging;
using Serilog;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Platforms
{
    /// <summary>
    /// DeepSeek 平台数据采集 Provider。
    /// 采集逻辑：每次从「当前日期往前两个自然月」的 1 日拉取至今日，按 api_key × model × day 入库；
    /// 用户信息来自 auth-api/users/current，账户余额/累计消费来自 get_user_summary。
    /// </summary>
    public sealed class DeepSeekPlatformProvider : IPlatformProvider
    {
        private readonly IDeepSeekApiClient _apiClient;
        private readonly ILogger<DeepSeekPlatformProvider> _logger;

        // RawJson 序列化专用 options：显式启用反射 TypeInfoResolver
        private static readonly JsonSerializerOptions RawJsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        /// <inheritdoc/>
        public PlatformType Platform => PlatformType.DeepSeek;

        /// <summary>
        /// 初始化 <see cref="DeepSeekPlatformProvider"/> 实例。
        /// </summary>
        /// <param name="apiClient">DeepSeek API 客户端。</param>
        /// <param name="logger">日志器。</param>
        public DeepSeekPlatformProvider(IDeepSeekApiClient apiClient, ILogger<DeepSeekPlatformProvider> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<Result<int>> TestConnectionAsync(string token, CancellationToken ct = default)
            => await _apiClient.TestConnectionAsync(token, ct);

        /// <inheritdoc/>
        public async Task<PlatformSyncResult> SyncAsync(string token, CancellationToken ct = default)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // 1. 拉取窗口：当前日期往前两个自然月的 1 日 → 明日（如 7.17 → 5.1；8.17 → 6.1）
            //    大屏「本月」仍用当前自然月范围
            var (pullStartSec, pullEndSec) = GetPullRangeUtc();
            var (monthStartSec, monthEndSec) = GetCurrentMonthRangeUtc();
            var monthStartMs = monthStartSec * 1000;

            // 2. 并发拉取：账户汇总 / 用量 / 费用 / Key 列表 / 当前用户
            var summaryTask = SafeGetAsync(() => _apiClient.GetUserSummaryAsync(token, ct));
            var amountTask = SafeGetAsync(() => _apiClient.GetUsageAmountAsync(token, pullStartSec, pullEndSec, ct));
            var costTask = SafeGetAsync(() => _apiClient.GetUsageCostAsync(token, pullStartSec, pullEndSec, ct));
            var keysTask = SafeGetAsync(() => _apiClient.GetApiKeysAsync(token, ct));
            var userTask = SafeGetAsync(() => _apiClient.GetCurrentUserAsync(token, ct));

            await Task.WhenAll(summaryTask, amountTask, costTask, keysTask, userTask);

            DeepSeekUserSummaryDto? summary = await summaryTask;
            DeepSeekUsageAmountDto? amount = await amountTask;
            DeepSeekUsageCostDto? cost = await costTask;
            DeepSeekApiKeysDto? keys = await keysTask;
            DeepSeekCurrentUserDto? currentUser = await userTask;

            // 3. 构建按 (day, trackingId, model) 索引的费用查找表
            var costLookup = BuildCostLookup(cost);

            // 4. 将每日用量桶映射为事件实体（按 api_key × model 区分存储，含 REQUEST）
            var entities = new List<UsageEventEntity>();
            long monthRequests = 0;
            if (amount?.Series is not null)
            {
                foreach (var series in amount.Series)
                {
                    var trackingId = series.ApiKey?.TrackingId
                                     ?? series.ApiKey?.SensitiveId
                                     ?? series.ApiKey?.Name
                                     ?? "unknown";
                    var apiKeyName = series.ApiKey?.Name
                                     ?? series.ApiKey?.SensitiveId
                                     ?? trackingId;
                    var model = series.Model ?? "unknown";
                    if (series.Buckets is null) continue;

                    foreach (var bucket in series.Buckets)
                    {
                        var usage = bucket.Usage;
                        if (usage is null) continue;

                        // 跳过全零的桶（减少无用数据）
                        if (usage.ResponseToken == 0 && usage.PromptCacheHitToken == 0
                            && usage.PromptCacheMissToken == 0 && usage.Request == 0)
                        {
                            continue;
                        }

                        var tsMs = bucket.Time * 1000;
                        var costYuan = costLookup.TryGetValue((bucket.Time, trackingId, model), out var c) ? c : 0m;
                        var costCents = costYuan * 100;

                        // 本月请求数仅统计当前自然月（大屏总请求次数）
                        if (tsMs >= monthStartMs)
                        {
                            monthRequests += usage.Request;
                        }

                        entities.Add(new UsageEventEntity
                        {
                            Platform = PlatformType.DeepSeek,
                            Timestamp = tsMs,
                            // UserEmail 存 API Key 显示名；Kind 存 tracking_id（稳定标识）
                            UserEmail = apiKeyName,
                            Model = model,
                            Kind = trackingId,
                            MaxMode = false,
                            RequestsCosts = 0,
                            RequestCount = usage.Request,
                            IsTokenBasedCall = true,
                            IsChargeable = costCents > 0,
                            IsHeadless = false,
                            // DeepSeek：缓存未命中=输入，缓存命中=缓存读取，响应=输出
                            InputTokens = usage.PromptCacheMissToken,
                            OutputTokens = usage.ResponseToken,
                            CacheReadTokens = usage.PromptCacheHitToken,
                            CacheWriteTokens = 0,
                            TotalCents = costCents,
                            CursorTokenFee = 0,
                            ChargedCents = costCents,
                            FetchTime = nowMs
                        });
                    }
                }
            }

            // 5. 周期汇总：本月消费 + 余额 + 本月请求数；累计消费写入 FastPremiumRequests（分）
            PeriodUsageEntity? periodEntity = null;
            if (summary is not null)
            {
                periodEntity = MapToPeriodEntity(summary, monthStartSec, monthEndSec, nowMs, monthRequests);
            }

            // 6. 用户信息来自 auth-api/users/current（不是 get_api_keys）
            UserInfoEntity? userEntity = MapToUserEntity(currentUser, keys, nowMs);

            // 7. 订阅位复用：存账户余额与累计消费（周期字段仍为本自然月）
            SubscriptionEntity? subEntity = null;
            if (summary is not null)
            {
                subEntity = MapToSubscriptionEntity(summary, currentUser, monthStartSec, monthEndSec, nowMs);
            }

            return new PlatformSyncResult
            {
                Platform = PlatformType.DeepSeek,
                UsageEvents = entities,
                PeriodUsage = periodEntity,
                UserInfo = userEntity,
                Subscription = subEntity,
                SessionExpiryUtc = null,
                SyncSummary = $"拉取 {entities.Count} 条日用量（自 {DateTimeOffset.FromUnixTimeSeconds(pullStartSec):yyyy-MM-dd}），本月请求 {monthRequests} 次"
            };
        }

        /// <summary>
        /// 用量拉取窗口（UTC 秒）：当前日期往前两个自然月的 1 日 00:00 → 明日 00:00。
        /// 例：7.17 → 5.1；8.17 → 6.1。
        /// </summary>
        private static (long startSec, long endSec) GetPullRangeUtc()
        {
            var utcNow = DateTime.UtcNow;
            var startMonth = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-2);
            var tomorrow = DateTime.SpecifyKind(utcNow.Date.AddDays(1), DateTimeKind.Utc);

            var startSec = new DateTimeOffset(startMonth).ToUnixTimeSeconds();
            var endSec = new DateTimeOffset(tomorrow).ToUnixTimeSeconds();
            return (startSec, endSec);
        }

        /// <summary>
        /// 当前自然月的 UTC 时间范围（秒），用于大屏「本月」聚合。
        /// start = 本月 1 日 00:00:00 UTC，end = 明日 00:00:00 UTC。
        /// </summary>
        private static (long startSec, long endSec) GetCurrentMonthRangeUtc()
        {
            var utcNow = DateTime.UtcNow;
            var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var tomorrow = DateTime.SpecifyKind(utcNow.Date.AddDays(1), DateTimeKind.Utc);

            var startSec = new DateTimeOffset(monthStart).ToUnixTimeSeconds();
            var endSec = new DateTimeOffset(tomorrow).ToUnixTimeSeconds();
            return (startSec, endSec);
        }

        /// <summary>
        /// 构建费用查找表：Key=(day 秒, trackingId, model)，Value=费用（元）。
        /// </summary>
        private static Dictionary<(long, string, string), decimal> BuildCostLookup(DeepSeekUsageCostDto? cost)
        {
            var lookup = new Dictionary<(long, string, string), decimal>();
            if (cost?.Data is null) return lookup;

            foreach (var currencyEntry in cost.Data)
            {
                if (currencyEntry.Series is null) continue;
                foreach (var series in currencyEntry.Series)
                {
                    var trackingId = series.ApiKey?.TrackingId
                                     ?? series.ApiKey?.SensitiveId
                                     ?? series.ApiKey?.Name
                                     ?? "unknown";
                    var model = series.Model ?? "unknown";
                    if (series.Buckets is null) continue;

                    foreach (var bucket in series.Buckets)
                    {
                        var costYuan = bucket.Cost ?? 0m;
                        if (costYuan == 0) continue;

                        var key = (bucket.Time, trackingId, model);
                        lookup[key] = lookup.TryGetValue(key, out var existing) ? existing + costYuan : costYuan;
                    }
                }
            }
            return lookup;
        }

        /// <summary>
        /// 将 get_user_summary 映射为周期汇总。
        /// TotalSpendCents=本月消费（分）；RemainingRequests=充值余额（分）；
        /// FastPremiumRequests=累计消费（分）；UsedRequests=本月 REQUEST 合计。
        /// </summary>
        private static PeriodUsageEntity MapToPeriodEntity(
            DeepSeekUserSummaryDto summary, long startSec, long endSec, long fetchMs, long totalRequests)
        {
            var monthlyTokens = summary.MonthlyUsage ?? summary.MonthlyTokenUsage ?? 0;
            var monthlyCostYuan = summary.MonthlyCosts?.FirstOrDefault()?.Amount ?? 0m;
            var totalCostYuan = summary.TotalCosts?.FirstOrDefault()?.Amount ?? 0m;
            var balanceYuan = summary.NormalWallets?.FirstOrDefault()?.Balance ?? 0m;

            return new PeriodUsageEntity
            {
                Platform = PlatformType.DeepSeek,
                FetchTime = fetchMs,
                PeriodStart = startSec * 1000,
                PeriodEnd = endSec * 1000,
                PlanName = "pay-as-you-go",
                IncludedRequests = 0,
                UsedRequests = totalRequests,
                UsedTokens = monthlyTokens,
                TotalSpendCents = monthlyCostYuan * 100,
                // DeepSeek 复用：累计消费金额（分）
                FastPremiumRequests = (long)(totalCostYuan * 100),
                // DeepSeek 复用：账户充值余额（分）
                RemainingRequests = (long)(balanceYuan * 100),
                RawJson = SafeSerialize(summary)
            };
        }

        /// <summary>
        /// 从 auth-api 用户信息映射 UserInfo；失败时回退到首个 API Key 名（兼容旧逻辑）。
        /// </summary>
        private static UserInfoEntity? MapToUserEntity(
            DeepSeekCurrentUserDto? currentUser, DeepSeekApiKeysDto? keys, long nowMs)
        {
            var email = currentUser?.Email
                        ?? currentUser?.MobileNumber
                        ?? keys?.ApiKeys?.FirstOrDefault()?.Name;
            if (email is null) return null;

            return new UserInfoEntity
            {
                Platform = PlatformType.DeepSeek,
                Email = email,
                Name = currentUser?.IdProfile?.Name ?? email,
                PlanName = "pay-as-you-go",
                Role = currentUser?.IdProfile?.Region,
                MonthlyLimitDollars = 0,
                HardLimitOverrideDollars = 0,
                SnapshotTime = nowMs
            };
        }

        /// <summary>
        /// 将账户余额/累计消费映射到订阅实体（DeepSeek 无 Stripe 订阅）。
        /// </summary>
        private static SubscriptionEntity MapToSubscriptionEntity(
            DeepSeekUserSummaryDto summary,
            DeepSeekCurrentUserDto? currentUser,
            long startSec,
            long endSec,
            long fetchMs)
        {
            var totalCostYuan = summary.TotalCosts?.FirstOrDefault()?.Amount ?? 0m;
            var balanceYuan = summary.NormalWallets?.FirstOrDefault()?.Balance ?? 0m;

            return new SubscriptionEntity
            {
                Platform = PlatformType.DeepSeek,
                SnapshotTime = fetchMs,
                Plan = "pay-as-you-go",
                Status = "active",
                CurrentPeriodStart = startSec * 1000,
                CurrentPeriodEnd = endSec * 1000,
                TrialEnd = 0,
                CancelAtPeriodEnd = false,
                SubscriptionStart = 0,
                InvoiceCount = 0,
                Email = currentUser?.Email,
                RawJson = SafeSerialize(new
                {
                    balanceYuan,
                    totalCostYuan,
                    monthlyCostYuan = summary.MonthlyCosts?.FirstOrDefault()?.Amount ?? 0m,
                    summary,
                    currentUser
                })
            };
        }

        /// <summary>
        /// 包裹单个 API 调用：认证/端点异常向上抛，其他失败转 null。
        /// </summary>
        private static async Task<T?> SafeGetAsync<T>(Func<Task<T>> factory) where T : class
        {
            try
            {
                return await factory();
            }
            catch (DeepSeekApiAuthException)
            {
                throw;
            }
            catch (DeepSeekApiEndpointChangedException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Logger.Warning(ex, "DeepSeek 辅助 API 调用失败（不影响事件入库）");
                return null;
            }
        }

        /// <summary>
        /// 安全序列化 DTO 为 JSON 字符串。
        /// </summary>
        private static string? SafeSerialize<T>(T value)
        {
            try
            {
                return JsonSerializer.Serialize(value, RawJsonOptions);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "DeepSeek RawJson 序列化失败（类型：{Type}）", typeof(T).Name);
                return null;
            }
        }
    }
}
