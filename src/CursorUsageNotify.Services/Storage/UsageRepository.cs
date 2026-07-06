using CursorUsageNotify.Core;
using CursorUsageNotify.Models.Entities;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace CursorUsageNotify.Services.Storage;

/// <summary>
/// 用量数据仓储实现，基于 SqlSugar Storageable 实现 upsert 去重。
/// </summary>
public sealed class UsageRepository : IUsageRepository
{
    private readonly ISqlSugarClient _db;
    private readonly ILogger<UsageRepository> _logger;

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

        var x = _db.Storageable(list)
            .WhereColumns(p => new { p.Timestamp, p.UserEmail })
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
            .WhereColumns(p => p.FetchTime)
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
            .WhereColumns(p => new { p.Email, p.SnapshotTime })
            .ToStorage();

        var inserted = await x.AsInsertable.ExecuteCommandAsync(ct);
        var updated = await x.AsUpdateable.ExecuteCommandAsync(ct);
        return inserted + updated;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<UsageEventEntity>> QueryEventsPagedAsync(
        long startTime = 0, long endTime = 0, string? model = null,
        int pageIndex = 1, int pageSize = 100, CancellationToken ct = default)
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
    public async Task<List<string>> GetDistinctModelsAsync(CancellationToken ct = default)
    {
        return await _db.Queryable<UsageEventEntity>()
            .Where(e => e.Model != null && e.Model != "")
            .Select(e => e.Model!)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<PeriodUsageEntity?> GetLatestPeriodUsageAsync(CancellationToken ct = default)
    {
        return await _db.Queryable<PeriodUsageEntity>()
            .OrderBy(e => e.FetchTime, OrderByType.Desc)
            .FirstAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<UserInfoEntity?> GetLatestUserInfoAsync(CancellationToken ct = default)
    {
        return await _db.Queryable<UserInfoEntity>()
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
            .WhereColumns(p => p.SnapshotTime)
            .ToStorage();

        var inserted = await x.AsInsertable.ExecuteCommandAsync(ct);
        var updated = await x.AsUpdateable.ExecuteCommandAsync(ct);
        return inserted + updated;
    }

    /// <inheritdoc/>
    public async Task<SubscriptionEntity?> GetLatestSubscriptionAsync(CancellationToken ct = default)
    {
        return await _db.Queryable<SubscriptionEntity>()
            .OrderBy(e => e.SnapshotTime, OrderByType.Desc)
            .FirstAsync(ct);
    }
}
