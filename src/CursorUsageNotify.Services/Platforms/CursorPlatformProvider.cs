using System.Text.Json;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Larpx.PersonalTools.CursorUsageNotify.Services.Scheduling;
using Microsoft.Extensions.Logging;
using Serilog;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Platforms
{
    /// <summary>
    /// Cursor 平台数据采集 Provider。
    /// 从 UsageSyncHostedService 提取 Cursor 专属的 API 调用与数据映射逻辑，
    /// 返回 <see cref="PlatformSyncResult"/> 供 HostedService 统一入库与通知。
    /// </summary>
    public sealed class CursorPlatformProvider : IPlatformProvider
    {
        private readonly ICursorApiClient _apiClient;
        private readonly UsageSyncOptions _options;
        private readonly ILogger<CursorPlatformProvider> _logger;

        // 辅助 API 批量并发超时
        private static readonly TimeSpan BatchTimeout = TimeSpan.FromSeconds(60);
        // 单次同步事件条数上限，防止内存膨胀
        private const int MaxEventsPerSync = 50000;
        // 分页安全上限
        private const int MaxPages = 50;

        // RawJson 序列化专用 options：显式启用反射 TypeInfoResolver，
        // 避免 .NET 10 在 publish 配置下默认 source-gen resolver 无法处理
        // ExtraFields（Dictionary<string, object>）反序列化后产生的 JsonElement。
        private static readonly JsonSerializerOptions RawJsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        /// <inheritdoc/>
        public PlatformType Platform => PlatformType.Cursor;

        /// <summary>
        /// 初始化 <see cref="CursorPlatformProvider"/> 实例。
        /// </summary>
        /// <param name="apiClient">Cursor API 客户端。</param>
        /// <param name="options">同步运行时配置（读取 LastSyncTimeMs 用于增量同步）。</param>
        /// <param name="logger">日志器。</param>
        public CursorPlatformProvider(
            ICursorApiClient apiClient,
            UsageSyncOptions options,
            ILogger<CursorPlatformProvider> logger)
        {
            _apiClient = apiClient;
            _options = options;
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task<Result<int>> TestConnectionAsync(string token, CancellationToken ct = default)
            => _apiClient.TestConnectionAsync(token, ct);

        /// <inheritdoc/>
        public async Task<PlatformSyncResult> SyncAsync(string token, CancellationToken ct = default)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var startTs = _options.LastSyncTimeMs > 0 ? _options.LastSyncTimeMs : 0;

            // 1. usage-events 分页拉取（主体，必须先完成）
            var allEvents = new List<CursorUsageEventDto>();
            var page = 1;
            const int pageSize = 500;
            int total;

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

            // 2. 并发拉取 6 个辅助 GET：period / stripe / profile / cycle / invoices / sessions
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
                throw; // 端点变更向上抛，由 HostedService 明确通知用户
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

            // 3. 映射为实体（入库由 HostedService 负责）
            var planName = stripeDto?.MembershipType;
            PeriodUsageEntity? periodEntity = periodDto is not null
                ? MapToPeriodEntity(periodDto, nowMs, planName)
                : null;

            SubscriptionEntity? subEntity = null;
            if (stripeDto is not null || cycleDto is not null || invoicesDto is not null)
            {
                subEntity = MapToSubscriptionEntity(stripeDto, cycleDto, invoicesDto, profileDto, periodDto, nowMs);
            }

            // 用户信息（优先 profile.handle/displayName，其次事件表邮箱，再其次 subscription）
            var userHandle = profileDto?.Profile?.Handle;
            var userDisplay = profileDto?.Profile?.DisplayName;
            var userEmail = userHandle
                            ?? allEvents.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.UserEmail))?.UserEmail
                            ?? subEntity?.Email;

            UserInfoEntity? userEntity = null;
            if (userEmail is not null)
            {
                userEntity = new UserInfoEntity
                {
                    Email = userEmail,
                    Name = userDisplay ?? userHandle,
                    PlanName = planName,
                    MonthlyLimitDollars = 0,
                    HardLimitOverrideDollars = 0,
                    SnapshotTime = nowMs
                };
            }

            return new PlatformSyncResult
            {
                Platform = PlatformType.Cursor,
                UsageEvents = entities,
                PeriodUsage = periodEntity,
                UserInfo = userEntity,
                Subscription = subEntity,
                SessionExpiryUtc = sessionsDto?.LatestWebSessionExpiryUtc,
                SyncSummary = $"拉取 {allEvents.Count} 条事件"
            };
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
        private static string? SafeSerialize<T>(T value)
        {
            try
            {
                return JsonSerializer.Serialize(value, RawJsonOptions);
            }
            catch (Exception ex)
            {
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
                TrialEnd = 0,
                CancelAtPeriodEnd = cancelAtPeriodEnd,
                SubscriptionStart = subStart,
                InvoiceCount = invoices?.Invoices?.Count ?? invoices?.Total ?? 0,
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
    }
}
