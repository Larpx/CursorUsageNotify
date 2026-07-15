using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Storage
{
    /// <summary>
    /// 用量数据仓储抽象，负责 upsert 去重和查询。
    /// 多平台支持：所有查询方法均接受 PlatformType 参数（默认 Cursor 向后兼容）。
    /// </summary>
    public interface IUsageRepository
    {
        /// <summary>
        /// 批量 upsert 用量事件（按 Timestamp + UserEmail + Platform 去重）。
        /// </summary>
        Task<int> UpsertUsageEventsAsync(IEnumerable<UsageEventEntity> events, CancellationToken ct = default);

        /// <summary>
        /// upsert 计费周期汇总（按 FetchTime + Platform 去重）。
        /// </summary>
        Task<int> UpsertPeriodUsageAsync(PeriodUsageEntity entity, CancellationToken ct = default);

        /// <summary>
        /// upsert 用户信息快照（按 Email + SnapshotTime + Platform 去重）。
        /// </summary>
        Task<int> UpsertUserInfoAsync(UserInfoEntity entity, CancellationToken ct = default);

        /// <summary>
        /// 分页查询用量事件。
        /// </summary>
        /// <param name="startTime">
        /// 起始时间（epoch 毫秒，0 表示不限）。
        /// </param>
        /// <param name="endTime">
        /// 结束时间（epoch 毫秒，0 表示不限）。
        /// </param>
        /// <param name="model">
        /// 模型筛选（null 表示不限）。
        /// </param>
        /// <param name="pageIndex">
        /// 页码（从 1 开始）。
        /// </param>
        /// <param name="pageSize">
        /// 每页大小。
        /// </param>
        /// <param name="platform">
        /// 平台筛选（默认 Cursor 向后兼容；传 null 查询所有平台）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<PagedResult<UsageEventEntity>> QueryEventsPagedAsync(
            long startTime = 0, long endTime = 0, string? model = null,
            int pageIndex = 1, int pageSize = 100,
            PlatformType? platform = PlatformType.Cursor,
            CancellationToken ct = default);

        /// <summary>
        /// 查询所有不同的模型名（用于筛选下拉）。
        /// </summary>
        /// <param name="platform">
        /// 平台筛选（默认 Cursor 向后兼容；传 null 查询所有平台）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<List<string>> GetDistinctModelsAsync(PlatformType? platform = PlatformType.Cursor, CancellationToken ct = default);

        /// <summary>
        /// 获取指定平台最近一条计费周期汇总（用于数据大屏）。
        /// </summary>
        /// <param name="platform">
        /// 平台类型（默认 Cursor 向后兼容）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<PeriodUsageEntity?> GetLatestPeriodUsageAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default);

        /// <summary>
        /// 获取指定平台最近一条用户信息（用于数据大屏）。
        /// </summary>
        /// <param name="platform">
        /// 平台类型（默认 Cursor 向后兼容）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<UserInfoEntity?> GetLatestUserInfoAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default);

        /// <summary>
        /// 清空所有平台的历史数据（用户手动触发）。
        /// </summary>
        Task ClearAllAsync(CancellationToken ct = default);

        /// <summary>
        /// upsert 订阅/账单信息快照（按 SnapshotTime + Platform 去重）。
        /// </summary>
        Task<int> UpsertSubscriptionAsync(SubscriptionEntity entity, CancellationToken ct = default);

        /// <summary>
        /// 获取指定平台最近一条订阅信息。
        /// </summary>
        /// <param name="platform">
        /// 平台类型（默认 Cursor 向后兼容）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<SubscriptionEntity?> GetLatestSubscriptionAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default);

        /// <summary>
        /// 按时间范围聚合统计指定平台的用量（从 usage_events 表求和 token、请求数、支出）。
        /// </summary>
        /// <param name="periodStart">
        /// 起始时间（epoch 毫秒）。
        /// </param>
        /// <param name="periodEnd">
        /// 结束时间（epoch 毫秒）。
        /// </param>
        /// <param name="platform">
        /// 平台类型（默认 Cursor 向后兼容）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<UsageAggregateStats> AggregateStatsAsync(
            long periodStart, long periodEnd,
            PlatformType platform = PlatformType.Cursor,
            CancellationToken ct = default);

        /// <summary>
        /// 聚合指定平台本周（周一 00:00 至今）的用量统计数据。
        /// </summary>
        /// <param name="platform">
        /// 平台类型（默认 Cursor 向后兼容）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<UsageAggregateStats> AggregateWeeklyStatsAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default);

        /// <summary>
        /// 获取指定平台范围内第一条事件的用户邮箱（用于推断账号信息）。
        /// </summary>
        /// <param name="platform">
        /// 平台类型（默认 Cursor 向后兼容）。
        /// </param>
        /// <param name="ct">
        /// 取消令牌。
        /// </param>
        Task<string?> GetFirstUserEmailAsync(PlatformType platform = PlatformType.Cursor, CancellationToken ct = default);
    }
}
