using SqlSugar;


namespace Larpx.PersonalTools.CursorUsageNotify.Models.Entities
{
    /// <summary>
    /// 单次 Cursor 用量事件（一次 API 调用 = 一行）。
    /// 去重键：Timestamp + UserEmail，避免定时任务重复插入脏数据。
    /// </summary>
    [SugarTable("usage_events")]
    public sealed class UsageEventEntity
    {
        /// <summary>
        /// 自增主键。SQLite 要求 AUTOINCREMENT 必须是 INTEGER PRIMARY KEY，故显式指定列类型。
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id", ColumnDataType = "INTEGER")]
        public long Id { get; set; }

        /// <summary>
        /// 事件发生时间（epoch 毫秒，来自 API 响应 timestamp 字段）。
        /// </summary>
        [SugarColumn(ColumnName = "timestamp", IsNullable = false)]
        public long Timestamp { get; set; }

        /// <summary>
        /// 触发事件的用户邮箱（个人账号场景即当前账号）。
        /// </summary>
        [SugarColumn(ColumnName = "user_email", IsNullable = false, Length = 256)]
        public string UserEmail { get; set; } = string.Empty;

        /// <summary>
        /// 调用的 AI 模型名（如 claude-4.5-sonnet、gpt-5）。
        /// </summary>
        [SugarColumn(ColumnName = "model", IsNullable = true, Length = 128)]
        public string? Model { get; set; }

        /// <summary>
        /// 计费类别（如 Usage-based、Included in Pro）。
        /// </summary>
        [SugarColumn(ColumnName = "kind", IsNullable = true, Length = 64)]
        public string? Kind { get; set; }

        /// <summary>
        /// 是否使用 max 模式。
        /// </summary>
        [SugarColumn(ColumnName = "max_mode")]
        public bool MaxMode { get; set; }

        /// <summary>
        /// 请求成本单位（请求计费模式下使用）。
        /// </summary>
        [SugarColumn(ColumnName = "requests_costs")]
        public decimal RequestsCosts { get; set; }

        /// <summary>
        /// 是否按 token 计费。
        /// </summary>
        [SugarColumn(ColumnName = "is_token_based_call")]
        public bool IsTokenBasedCall { get; set; }

        /// <summary>
        /// 是否产生费用。
        /// </summary>
        [SugarColumn(ColumnName = "is_chargeable")]
        public bool IsChargeable { get; set; }

        /// <summary>
        /// 是否为无客户端的后台请求（如 background agents）。
        /// </summary>
        [SugarColumn(ColumnName = "is_headless")]
        public bool IsHeadless { get; set; }

        /// <summary>
        /// 输入 token 数。
        /// </summary>
        [SugarColumn(ColumnName = "input_tokens")]
        public long InputTokens { get; set; }

        /// <summary>
        /// 输出 token 数。
        /// </summary>
        [SugarColumn(ColumnName = "output_tokens")]
        public long OutputTokens { get; set; }

        /// <summary>
        /// 缓存读取 token 数。
        /// </summary>
        [SugarColumn(ColumnName = "cache_read_tokens")]
        public long CacheReadTokens { get; set; }

        /// <summary>
        /// 缓存写入 token 数。
        /// </summary>
        [SugarColumn(ColumnName = "cache_write_tokens")]
        public long CacheWriteTokens { get; set; }

        /// <summary>
        /// 本次事件总费用（美分）。
        /// </summary>
        [SugarColumn(ColumnName = "total_cents")]
        public decimal TotalCents { get; set; }

        /// <summary>
        /// Cursor Token Fee（若启用）。
        /// </summary>
        [SugarColumn(ColumnName = "cursor_token_fee")]
        public decimal CursorTokenFee { get; set; }

        /// <summary>
        /// 实际计费金额（美分，含 model cost + token fee）。
        /// </summary>
        [SugarColumn(ColumnName = "charged_cents")]
        public decimal ChargedCents { get; set; }

        /// <summary>
        /// 本行入库时间（epoch 毫秒）。
        /// </summary>
        [SugarColumn(ColumnName = "fetch_time")]
        public long FetchTime { get; set; }
    }
}
