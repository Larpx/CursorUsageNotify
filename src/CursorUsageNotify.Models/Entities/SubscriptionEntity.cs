using SqlSugar;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Entities
{
    /// <summary>
    /// 订阅/账单信息快照，支持多平台。
    /// 从 /dashboard/billing 页面抓取，包含订阅起始日期、状态、发票历史等。
    /// 去重键：SnapshotTime（按小时截断）+ Platform。
    /// </summary>
    [SugarTable("subscription_info")]
    public sealed class SubscriptionEntity
    {
        /// <summary>
        /// 自增主键。
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id", ColumnDataType = "INTEGER")]
        public long Id { get; set; }

        /// <summary>
        /// 数据来源平台（0=Cursor, 1=DeepSeek）。默认 0 保持向后兼容。
        /// </summary>
        [SugarColumn(ColumnName = "platform", IsNullable = false)]
        public PlatformType Platform { get; set; } = PlatformType.Cursor;

        /// <summary>
        /// 快照时间（epoch 毫秒，按小时截断作为去重键）。
        /// </summary>
        [SugarColumn(ColumnName = "snapshot_time", IsNullable = false)]
        public long SnapshotTime { get; set; }

        /// <summary>
        /// 订阅计划（Pro/Ultra 等）。
        /// </summary>
        [SugarColumn(ColumnName = "plan", IsNullable = true, Length = 64)]
        public string? Plan { get; set; }

        /// <summary>
        /// 订阅状态（active/trialing/canceled 等）。
        /// </summary>
        [SugarColumn(ColumnName = "status", IsNullable = true, Length = 32)]
        public string? Status { get; set; }

        /// <summary>
        /// 当前周期开始（epoch 毫秒）。
        /// </summary>
        [SugarColumn(ColumnName = "current_period_start")]
        public long CurrentPeriodStart { get; set; }

        /// <summary>
        /// 当前周期结束（epoch 毫秒）。
        /// </summary>
        [SugarColumn(ColumnName = "current_period_end")]
        public long CurrentPeriodEnd { get; set; }

        /// <summary>
        /// 试用结束时间（epoch 毫秒，0 表示无试用）。
        /// </summary>
        [SugarColumn(ColumnName = "trial_end")]
        public long TrialEnd { get; set; }

        /// <summary>
        /// 是否在周期结束时取消。
        /// </summary>
        [SugarColumn(ColumnName = "cancel_at_period_end")]
        public bool CancelAtPeriodEnd { get; set; }

        /// <summary>
        /// 首次订阅时间（epoch 毫秒，从最早发票日期推断）。
        /// </summary>
        [SugarColumn(ColumnName = "subscription_start")]
        public long SubscriptionStart { get; set; }

        /// <summary>
        /// 发票总数。
        /// </summary>
        [SugarColumn(ColumnName = "invoice_count")]
        public int InvoiceCount { get; set; }

        /// <summary>
        /// 用户邮箱。
        /// </summary>
        [SugarColumn(ColumnName = "email", IsNullable = true, Length = 256)]
        public string? Email { get; set; }

        /// <summary>
        /// 原始 JSON（保留完整响应）。
        /// </summary>
        [SugarColumn(ColumnName = "raw_json", IsNullable = true, ColumnDataType = "TEXT")]
        public string? RawJson { get; set; }
    }
}
