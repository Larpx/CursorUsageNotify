using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;
using Microsoft.Extensions.Logging;
using SqlSugar;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Storage
{
    /// <summary>
    /// 用量数据仓储实现，基于 SqlSugar Storageable 实现 upsert 去重。
    /// 多平台支持：所有查询与 upsert 均按 Platform 字段区分。
    /// </summary>
    public sealed class UsageRepository : IUsageRepository
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<UsageRepository> _logger;

        /// <summary>
        /// 初始化 <see cref="UsageRepository"/> 实例。
        /// </summary>
        public UsageRepository(IDbContext context, ILogger<UsageRepository> logger)
        {
            _db = context.Client;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<int> UpsertUsageEventsAsync(IEnumerable<UsageEventEntity> events, CancellationToken ct = default)
        {
            var list = events.ToList();
            if (list.Count == 0)
            {
                return 0;
            }

            // 含 Model：DeepSeek 同日同 Key 多模型需分存，不能互相覆盖
            var x = _db.Storageable(list)
                .WhereColumns(p => new { p.Timestamp, p.UserEmail, p.Platform, p.Model })
                .ToStorage();

            var inserted = await x.AsInsertable.ExecuteCommandAsync(ct);
            var updated = await x.AsUpdateable.ExecuteCommandAsync(ct);

            _logger.LogInformation("用量事件 upsert：插入 {Inserted}，更新 {Updated}", inserted, updated);
            return inserted + updated;
        }

        /// <inheritdoc/>
        public async Task<int> UpsertPeriodUsageAsync(PeriodUsageEntity entity, CancellationToken ct = default)
        {
            // FetchTime 按秒截断，避免同一秒内重复入库
            entity.FetchTime = (entity.FetchTime / 1000) * 1000;

            var x = _db.Storageable(entity)
                .WhereColumns(p => new { p.FetchTime, p.Platform })
                .ToStorage();

            var inserted = await x.AsInsertable.ExecuteCommandAsync(ct);
            var updated = await x.AsUpdateable.ExecuteCommandAsync(ct);
            _logger.LogInformation("计费周期汇总 upsert：插入 {Inserted}，更新 {Updated}", inserted, updated);
            return inserted + updated;
        }

        /// <inheritdoc/>
        public async Task<int> UpsertUserInfoAsync(UserInfoEntity entity, CancellationToken ct = default)
        {
            // SnapshotTime 按分钟截断
            entity.SnapshotTime = (entity.SnapshotTime / 60000) * 60000;

            var x = _db.Storageable(entity)
                .WhereColumns(p => new { p.Email, p.SnapshotTime, p.Platform })
                .ToStorage();

            var inserted = await x.AsInsertable.ExecuteCommandAsync(ct);
            var updated = await x.AsUpdateable.ExecuteCommandAsync(ct);
            return inserted + updated;
        }

        /// <inheritdoc/>
        public async Task<PagedResult<UsageEventEntity>> QueryEventsPagedAsync(
            long startTime = 0, long endTime = 0, string? model = null,
            int pageIndex = 1, int pageSize = 100,
            PlatformType? platform = PlatformType.Cursor,
            CancellationToken ct = default)
        {
            var query = _db.Queryable<UsageEventEntity>();

            if (startTime > 0)
            {
                query = query.Where(e => e.Timestamp >= startTime);
            }
            if (endTime > 0)
            {
                query = query.Where(e => e.Timestamp <= endTime);
            }
            if (!string.IsNullOrEmpty(model))
            {
                query = query.Where(e => e.Model == model);
            }
            if (platform is not null)
            {
                query = query.Where(e => e.Platform == platform);
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(e => e.Timestamp, OrderByType.Desc)
                                   .Skip((pageIndex - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync(ct);

            return new PagedResult<UsageEventEntity>
            {
                Items = items,
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        /// <inheritdoc/>
        public async Task<List<string>> GetDistinctModelsAsync(PlatformType? platform = PlatformType.Cursor, CancellationToken ct = default)
        {
            var query = _db.Queryable<UsageEventEntity>()
                .Where(e => e.Model != null && e.Model != "");
            if (platform is not null)
            {
                query = query.Where(e => e.Platform == platform);
            }
            return await query.Select(e => e.Model!)
                .Distinct()
                .ToListAsync(ct);
        }

        /// <inheritdoc/>
        public async Task<PeriodUsageEntity?> GetLatestPeriodUsageAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default)
        {
            return await _db.Queryable<PeriodUsageEntity>()
                .Where(e => e.Platform == platform)
                .OrderBy(e => e.FetchTime, OrderByType.Desc)
                .FirstAsync(ct);
        }

        /// <inheritdoc/>
        public async Task<UserInfoEntity?> GetLatestUserInfoAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default)
        {
            return await _db.Queryable<UserInfoEntity>()
                .Where(e => e.Platform == platform)
                .OrderBy(e => e.SnapshotTime, OrderByType.Desc)
                .FirstAsync(ct);
        }

        /// <inheritdoc/>
        public async Task ClearAllAsync(CancellationToken ct = default)
        {
            await _db.Deleteable<UsageEventEntity>().ExecuteCommandAsync(ct);
            await _db.Deleteable<PeriodUsageEntity>().ExecuteCommandAsync(ct);
            await _db.Deleteable<UserInfoEntity>().ExecuteCommandAsync(ct);
            await _db.Deleteable<SubscriptionEntity>().ExecuteCommandAsync(ct);
            _logger.LogWarning("用户已清空所有历史用量数据");
        }

        /// <inheritdoc/>
        public async Task<int> UpsertSubscriptionAsync(SubscriptionEntity entity, CancellationToken ct = default)
        {
            // SnapshotTime 按小时截断
            entity.SnapshotTime = (entity.SnapshotTime / 3600000) * 3600000;

            var x = _db.Storageable(entity)
                .WhereColumns(p => new { p.SnapshotTime, p.Platform })
                .ToStorage();

            var inserted = await x.AsInsertable.ExecuteCommandAsync(ct);
            var updated = await x.AsUpdateable.ExecuteCommandAsync(ct);
            return inserted + updated;
        }

        /// <inheritdoc/>
        public async Task<SubscriptionEntity?> GetLatestSubscriptionAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default)
        {
            return await _db.Queryable<SubscriptionEntity>()
                .Where(e => e.Platform == platform)
                .OrderBy(e => e.SnapshotTime, OrderByType.Desc)
                .FirstAsync(ct);
        }

        /// <inheritdoc/>
        public async Task<UsageAggregateStats> AggregateStatsAsync(
            long periodStart, long periodEnd,
            PlatformType platform = PlatformType.Cursor,
            string? apiKeyFilter = null,
            CancellationToken ct = default)
        {
            if (periodStart <= 0 || periodEnd <= 0 || periodStart >= periodEnd)
            {
                return new UsageAggregateStats { PeriodStart = periodStart, PeriodEnd = periodEnd };
            }

            var query = _db.Queryable<UsageEventEntity>()
                .Where(e => e.Platform == platform)
                .Where(e => e.Timestamp >= periodStart && e.Timestamp <= periodEnd);

            if (!string.IsNullOrWhiteSpace(apiKeyFilter))
            {
                query = query.Where(e => e.Kind == apiKeyFilter || e.UserEmail == apiKeyFilter);
            }

            var events = await query.ToListAsync(ct);

            // RequestCount：DeepSeek 存 usage.REQUEST；旧数据/未填时按 1（Cursor 单事件）计
            var totalRequests = events.Sum(e => e.RequestCount > 0 ? e.RequestCount : 1);

            return new UsageAggregateStats
            {
                TotalTokens = events.Sum(e => e.InputTokens + e.OutputTokens + e.CacheReadTokens + e.CacheWriteTokens),
                TotalRequests = (int)Math.Min(totalRequests, int.MaxValue),
                TotalSpendCents = events.Sum(e => e.ChargedCents),
                TotalInputTokens = events.Sum(e => e.InputTokens),
                TotalOutputTokens = events.Sum(e => e.OutputTokens),
                TotalCacheReadTokens = events.Sum(e => e.CacheReadTokens),
                TotalCacheWriteTokens = events.Sum(e => e.CacheWriteTokens),
                TokenBasedRequests = events.Count(e => e.IsTokenBasedCall),
                CostBasedRequests = events.Count(e => !e.IsTokenBasedCall),
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                UserEmail = events.FirstOrDefault(e => !string.IsNullOrEmpty(e.UserEmail))?.UserEmail
            };
        }

        /// <inheritdoc/>
        public async Task<string?> GetFirstUserEmailAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default)
        {
            var first = await _db.Queryable<UsageEventEntity>()
                .Where(e => e.Platform == platform)
                .OrderBy(e => e.Timestamp, OrderByType.Asc)
                .FirstAsync(ct);
            return first?.UserEmail;
        }

        /// <inheritdoc/>
        public async Task<UsageAggregateStats> AggregateWeeklyStatsAsync(
            PlatformType platform = PlatformType.Cursor,
            string? apiKeyFilter = null,
            CancellationToken ct = default)
        {
            var now = DateTime.Now;
            var daysSinceMonday = now.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)now.DayOfWeek - (int)DayOfWeek.Monday;
            var monday = now.Date.AddDays(-daysSinceMonday);
            // monday.Kind == Local，需用本地 offset 构造，否则 DateTimeOffset 抛 ArgumentException
            var weekStart = new DateTimeOffset(monday, TimeZoneInfo.Local.GetUtcOffset(monday)).ToUnixTimeMilliseconds();
            var weekEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return await AggregateStatsAsync(weekStart, weekEnd, platform, apiKeyFilter, ct);
        }

        /// <inheritdoc/>
        public async Task<UsageAggregateStats> AggregateDailyStatsAsync(
            PlatformType platform = PlatformType.Cursor,
            string? apiKeyFilter = null,
            CancellationToken ct = default)
        {
            var today = DateTime.Now.Date;
            var dayStart = new DateTimeOffset(today, TimeZoneInfo.Local.GetUtcOffset(today)).ToUnixTimeMilliseconds();
            var dayEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return await AggregateStatsAsync(dayStart, dayEnd, platform, apiKeyFilter, ct);
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<ApiKeyModelUsageBreakdown>> AggregateByApiKeyAndModelAsync(
            long periodStart, long periodEnd,
            PlatformType platform = PlatformType.DeepSeek,
            string? apiKeyFilter = null,
            CancellationToken ct = default)
        {
            if (periodStart <= 0 || periodEnd <= 0 || periodStart >= periodEnd)
            {
                return Array.Empty<ApiKeyModelUsageBreakdown>();
            }

            var query = _db.Queryable<UsageEventEntity>()
                .Where(e => e.Platform == platform)
                .Where(e => e.Timestamp >= periodStart && e.Timestamp <= periodEnd);

            if (!string.IsNullOrWhiteSpace(apiKeyFilter))
            {
                query = query.Where(e => e.Kind == apiKeyFilter || e.UserEmail == apiKeyFilter);
            }

            var events = await query.ToListAsync(ct);

            return events
                .GroupBy(e => new
                {
                    TrackingId = e.Kind ?? string.Empty,
                    Name = e.UserEmail ?? string.Empty,
                    Model = e.Model ?? "unknown"
                })
                .Select(g => new ApiKeyModelUsageBreakdown
                {
                    ApiKeyTrackingId = g.Key.TrackingId,
                    ApiKeyName = string.IsNullOrEmpty(g.Key.Name) ? g.Key.TrackingId : g.Key.Name,
                    Model = g.Key.Model,
                    InputTokens = g.Sum(e => e.InputTokens),
                    OutputTokens = g.Sum(e => e.OutputTokens),
                    CacheReadTokens = g.Sum(e => e.CacheReadTokens),
                    TotalTokens = g.Sum(e => e.InputTokens + e.OutputTokens + e.CacheReadTokens + e.CacheWriteTokens),
                    RequestCount = g.Sum(e => e.RequestCount > 0 ? e.RequestCount : 1),
                    SpendCents = g.Sum(e => e.ChargedCents)
                })
                .OrderBy(x => x.ApiKeyName)
                .ThenBy(x => x.Model)
                .ToList();
        }

        /// <inheritdoc/>
        public async Task<IReadOnlyList<(string TrackingId, string Name)>> GetDistinctApiKeysAsync(
            PlatformType platform = PlatformType.DeepSeek,
            CancellationToken ct = default)
        {
            var rows = await _db.Queryable<UsageEventEntity>()
                .Where(e => e.Platform == platform)
                .Where(e => e.UserEmail != null && e.UserEmail != "")
                .Select(e => new { e.Kind, e.UserEmail })
                .Distinct()
                .ToListAsync(ct);

            return rows
                .GroupBy(r => r.Kind ?? r.UserEmail ?? string.Empty)
                .Select(g =>
                {
                    var name = g.Select(x => x.UserEmail).FirstOrDefault(n => !string.IsNullOrEmpty(n))
                               ?? g.Key;
                    return (TrackingId: g.Key, Name: name!);
                })
                .Where(x => !string.IsNullOrEmpty(x.TrackingId))
                .OrderBy(x => x.Name)
                .ToList();
        }
    }
}
