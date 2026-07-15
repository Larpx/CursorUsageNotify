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
    /// 采集逻辑：每次拉取本月全量数据（1 日至明日 UTC 午夜），按 api_key × model × day 聚合为事件实体。
    /// DeepSeek 无订阅周期、无缓存写入 token、无 cookie 过期概念。
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

            // 1. 计算本月时间范围（UTC 自然月：1 日 00:00 至明日 00:00）
            var (startSec, endSec) = GetCurrentMonthRangeUtc();

            // 2. 并发拉取 4 个端点
            var summaryTask = SafeGetAsync(() => _apiClient.GetUserSummaryAsync(token, ct));
            var amountTask = SafeGetAsync(() => _apiClient.GetUsageAmountAsync(token, startSec, endSec, ct));
            var costTask = SafeGetAsync(() => _apiClient.GetUsageCostAsync(token, startSec, endSec, ct));
            var keysTask = SafeGetAsync(() => _apiClient.GetApiKeysAsync(token, ct));

            await Task.WhenAll(summaryTask, amountTask, costTask, keysTask);

            DeepSeekUserSummaryDto? summary = await summaryTask;
            DeepSeekUsageAmountDto? amount = await amountTask;
            DeepSeekUsageCostDto? cost = await costTask;
            DeepSeekApiKeysDto? keys = await keysTask;

            // 3. 构建按 (day, apiKey, model) 索引的费用查找表
            var costLookup = BuildCostLookup(cost);

            // 4. 将每日用量桶映射为事件实体
            var entities = new List<UsageEventEntity>();
            if (amount?.Series is not null)
            {
                foreach (var series in amount.Series)
                {
                    var apiKeyName = series.ApiKey?.Name ?? "unknown";
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

                        // 时间戳从秒转毫秒
                        var tsMs = bucket.Time * 1000;
                        // 查找对应费用（CNY 元 → 分）
                        var costYuan = costLookup.TryGetValue((bucket.Time, apiKeyName, model), out var c) ? c : 0m;
                        var costCents = (decimal)(costYuan * 100);

                        entities.Add(new UsageEventEntity
                        {
                            Platform = PlatformType.DeepSeek,
                            Timestamp = tsMs,
                            UserEmail = apiKeyName,
                            Model = model,
                            Kind = "api",
                            MaxMode = false,
                            RequestsCosts = 0,
                            IsTokenBasedCall = true,
                            IsChargeable = costCents > 0,
                            IsHeadless = false,
                            // DeepSeek token 映射：缓存未命中=输入，缓存命中=缓存读取，响应=输出
                            InputTokens = usage.PromptCacheMissToken,
                            OutputTokens = usage.ResponseToken,
                            CacheReadTokens = usage.PromptCacheHitToken,
                            CacheWriteTokens = 0, // DeepSeek 无缓存写入
                            TotalCents = costCents,
                            CursorTokenFee = 0,
                            ChargedCents = costCents,
                            FetchTime = nowMs
                        });
                    }
                }
            }

            // 5. 映射周期汇总实体（DeepSeek 按自然月统计）
            PeriodUsageEntity? periodEntity = null;
            if (summary is not null)
            {
                periodEntity = MapToPeriodEntity(summary, startSec, endSec, nowMs);
            }

            // 6. 映射用户信息（用 API Key 名称作为用户标识）
            UserInfoEntity? userEntity = null;
            var firstKeyName = keys?.ApiKeys?.FirstOrDefault()?.Name;
            if (firstKeyName is not null)
            {
                userEntity = new UserInfoEntity
                {
                    Platform = PlatformType.DeepSeek,
                    Email = firstKeyName,
                    Name = firstKeyName,
                    PlanName = null,
                    Role = null,
                    MonthlyLimitDollars = null,
                    HardLimitOverrideDollars = null,
                    SnapshotTime = nowMs
                };
            }

            // 7. 映射订阅/账单信息（DeepSeek 无订阅，存储账户余额）
            SubscriptionEntity? subEntity = null;
            if (summary is not null)
            {
                subEntity = MapToSubscriptionEntity(summary, startSec, endSec, nowMs);
            }

            return new PlatformSyncResult
            {
                Platform = PlatformType.DeepSeek,
                UsageEvents = entities,
                PeriodUsage = periodEntity,
                UserInfo = userEntity,
                Subscription = subEntity,
                SessionExpiryUtc = null, // DeepSeek token 无 cookie 过期概念
                SyncSummary = $"拉取 {entities.Count} 条日用量记录"
            };
        }

        /// <summary>
        /// 获取当前自然月的 UTC 时间范围（秒）。
        /// start = 本月 1 日 00:00:00 UTC，end = 明日 00:00:00 UTC（包含今天）。
        /// </summary>
        private static (long startSec, long endSec) GetCurrentMonthRangeUtc()
        {
            var utcNow = DateTime.UtcNow;
            var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var tomorrow = utcNow.Date.AddDays(1).AddTicks(-1).AddTicks(1); // 明日 00:00:00 UTC
            // 确保 tomorrow 是 UTC
            tomorrow = DateTime.SpecifyKind(tomorrow.Date, DateTimeKind.Utc);

            var startSec = new DateTimeOffset(monthStart).ToUnixTimeSeconds();
            var endSec = new DateTimeOffset(tomorrow).ToUnixTimeSeconds();
            return (startSec, endSec);
        }

        /// <summary>
        /// 构建费用查找表：Key=(day 时间戳秒, apiKeyName, model)，Value=费用（元）。
        /// 用于将 amount 端点的用量桶与 cost 端点的费用桶匹配。
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
                    var apiKeyName = series.ApiKey?.Name ?? "unknown";
                    var model = series.Model ?? "unknown";
                    if (series.Buckets is null) continue;

                    foreach (var bucket in series.Buckets)
                    {
                        var costYuan = bucket.Cost ?? 0m;
                        if (costYuan == 0) continue;

                        // 同一天同一 key 同一模型可能有多个币种，累加（实际 DeepSeek 通常单币种 CNY）
                        var key = (bucket.Time, apiKeyName, model);
                        lookup[key] = lookup.TryGetValue(key, out var existing) ? existing + costYuan : costYuan;
                    }
                }
            }
            return lookup;
        }

        /// <summary>
        /// 将 get_user_summary 映射为周期汇总实体。
        /// DeepSeek 按自然月统计，费用为 CNY（存储为分）。
        /// </summary>
        private static PeriodUsageEntity MapToPeriodEntity(
            DeepSeekUserSummaryDto summary, long startSec, long endSec, long fetchMs)
        {
            var monthlyTokens = summary.MonthlyUsage ?? 0;
            var monthlyCostYuan = summary.MonthlyCosts?.FirstOrDefault()?.Amount ?? 0m;
            var monthlyCostCents = monthlyCostYuan * 100;
            var balanceYuan = summary.NormalWallets?.FirstOrDefault()?.Balance ?? 0m;
            var balanceCents = balanceYuan * 100;

            return new PeriodUsageEntity
            {
                Platform = PlatformType.DeepSeek,
                FetchTime = fetchMs,
                PeriodStart = startSec * 1000,
                PeriodEnd = endSec * 1000,
                PlanName = null,
                IncludedRequests = 0,
                UsedRequests = 0,
                UsedTokens = monthlyTokens,
                TotalSpendCents = monthlyCostCents,
                FastPremiumRequests = 0,
                // remaining 复用为账户余额（分）
                RemainingRequests = (long)balanceCents,
                RawJson = SafeSerialize(summary)
            };
        }

        /// <summary>
        /// 将 get_user_summary 映射为订阅实体。
        /// DeepSeek 无订阅概念，存储账户余额与总费用信息。
        /// </summary>
        private static SubscriptionEntity MapToSubscriptionEntity(
            DeepSeekUserSummaryDto summary, long startSec, long endSec, long fetchMs)
        {
            var totalCostYuan = summary.TotalCosts?.FirstOrDefault()?.Amount ?? 0m;
            var totalCostCents = totalCostYuan * 100;
            var balanceYuan = summary.NormalWallets?.FirstOrDefault()?.Balance ?? 0m;
            var balanceCents = balanceYuan * 100;

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
                Email = null,
                RawJson = SafeSerialize(new
                {
                    totalCostCents,
                    balanceCents,
                    summary
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
