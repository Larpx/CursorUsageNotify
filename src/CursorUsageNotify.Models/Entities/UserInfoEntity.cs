using SqlSugar;
using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Entities
{
    /// <summary>
    /// 用户/订阅信息快照（用于数据大屏展示），支持多平台。
    /// 去重键：Email + SnapshotTime（按分钟截断）+ Platform。
    /// </summary>
    [SugarTable("user_info")]
    public sealed class UserInfoEntity
    {
        /// <summary>
        /// 自增主键。SQLite 要求 AUTOINCREMENT 必须是 INTEGER PRIMARY KEY，故显式指定列类型。
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id", ColumnDataType = "INTEGER")]
        public long Id { get; set; }

        /// <summary>
        /// 数据来源平台（0=Cursor, 1=DeepSeek）。默认 0 保持向后兼容。
        /// </summary>
        [SugarColumn(ColumnName = "platform", IsNullable = false)]
        public PlatformType Platform { get; set; } = PlatformType.Cursor;

        /// <summary>
        /// 用户邮箱。
        /// </summary>
        [SugarColumn(ColumnName = "email", IsNullable = false, Length = 256)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// 显示名（可能为空）。
        /// </summary>
        [SugarColumn(ColumnName = "name", IsNullable = true, Length = 128)]
        public string? Name { get; set; }

        /// <summary>
        /// 订阅计划名。
        /// </summary>
        [SugarColumn(ColumnName = "plan_name", IsNullable = true, Length = 64)]
        public string? PlanName { get; set; }

        /// <summary>
        /// 角色（个人账号通常为 owner）。
        /// </summary>
        [SugarColumn(ColumnName = "role", IsNullable = true, Length = 32)]
        public string? Role { get; set; }

        /// <summary>
        /// 月度支出限额（美元）。
        /// </summary>
        [SugarColumn(ColumnName = "monthly_limit_dollars", IsNullable = true)]
        public decimal? MonthlyLimitDollars { get; set; }

        /// <summary>
        /// 硬性限额覆盖（美元，0 表示无覆盖）。
        /// DeepSeek 无此概念，存储 null。
        /// </summary>
        [SugarColumn(ColumnName = "hard_limit_override_dollars", IsNullable = true)]
        public decimal? HardLimitOverrideDollars { get; set; }

        /// <summary>
        /// 快照时间（epoch 毫秒）。
        /// </summary>
        [SugarColumn(ColumnName = "snapshot_time")]
        public long SnapshotTime { get; set; }
    }
}
