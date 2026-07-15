using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Models.Entities;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Platforms
{
    /// <summary>
    /// 平台同步结果：由 IPlatformProvider.SyncAsync 返回，包含本次拉取的所有实体数据。
    /// UsageSyncHostedService 负责统一入库、聚合统计与通知。
    /// </summary>
    public sealed class PlatformSyncResult
    {
        /// <summary>
        /// 数据来源平台。
        /// </summary>
        public required PlatformType Platform { get; init; }

        /// <summary>
        /// 本次拉取的用量事件实体列表（已映射，可直接入库）。
        /// </summary>
        public required IReadOnlyList<UsageEventEntity> UsageEvents { get; init; }

        /// <summary>
        /// 周期用量快照（null 表示本次未拉取到）。
        /// </summary>
        public PeriodUsageEntity? PeriodUsage { get; init; }

        /// <summary>
        /// 用户信息快照（null 表示本次未拉取到）。
        /// </summary>
        public UserInfoEntity? UserInfo { get; init; }

        /// <summary>
        /// 订阅信息快照（Cursor 有，DeepSeek 无；null 表示不适用或未拉取到）。
        /// </summary>
        public SubscriptionEntity? Subscription { get; init; }

        /// <summary>
        /// 会话过期时间（UTC），仅 Cursor 有；用于检测 cookie 即将过期。
        /// </summary>
        public DateTime? SessionExpiryUtc { get; init; }

        /// <summary>
        /// 同步摘要文本（用于日志记录）。
        /// </summary>
        public string? SyncSummary { get; init; }
    }
}
