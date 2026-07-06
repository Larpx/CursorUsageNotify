using CursorUsageNotify.Core;
using CursorUsageNotify.Models.Entities;

namespace CursorUsageNotify.Services.Storage;

/// <summary>
/// 用量数据仓储抽象，负责 upsert 去重和查询。
/// </summary>
public interface IUsageRepository
{
    /// <summary>
    /// 批量 upsert 用量事件（按 Timestamp + UserEmail 去重）。
    /// </summary>
    Task<int> UpsertUsageEventsAsync(IEnumerable<UsageEventEntity> events, CancellationToken ct = default);

    /// <summary>
    /// upsert 计费周期汇总（按 FetchTime 按秒截断去重）。
    /// </summary>
    Task<int> UpsertPeriodUsageAsync(PeriodUsageEntity entity, CancellationToken ct = default);

    /// <summary>
    /// upsert 用户信息快照（按 Email + SnapshotTime 按分钟截断去重）。
    /// </summary>
    Task<int> UpsertUserInfoAsync(UserInfoEntity entity, CancellationToken ct = default);

    /// <summary>
    /// 分页查询用量事件。
    /// </summary>
    /// <param name="startTime">起始时间（epoch 毫秒，0 表示不限）。</param>
    /// <param name="endTime">结束时间（epoch 毫秒，0 表示不限）。</param>
    /// <param name="model">模型筛选（null 表示不限）。</param>
    /// <param name="pageIndex">页码（从 1 开始）。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PagedResult<UsageEventEntity>> QueryEventsPagedAsync(
        long startTime = 0, long endTime = 0, string? model = null,
        int pageIndex = 1, int pageSize = 100, CancellationToken ct = default);

    /// <summary>
    /// 查询所有不同的模型名（用于筛选下拉）。
    /// </summary>
    Task<List<string>> GetDistinctModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取最近一条计费周期汇总（用于数据大屏）。
    /// </summary>
    Task<PeriodUsageEntity?> GetLatestPeriodUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取最近一条用户信息（用于数据大屏）。
    /// </summary>
    Task<UserInfoEntity?> GetLatestUserInfoAsync(CancellationToken ct = default);

    /// <summary>
    /// 清空所有历史数据（用户手动触发）。
    /// </summary>
    Task ClearAllAsync(CancellationToken ct = default);

    /// <summary>
    /// upsert 订阅/账单信息快照（按 SnapshotTime 按小时截断去重）。
    /// </summary>
    Task<int> UpsertSubscriptionAsync(SubscriptionEntity entity, CancellationToken ct = default);

    /// <summary>
    /// 获取最近一条订阅信息。
    /// </summary>
    Task<SubscriptionEntity?> GetLatestSubscriptionAsync(CancellationToken ct = default);
}
