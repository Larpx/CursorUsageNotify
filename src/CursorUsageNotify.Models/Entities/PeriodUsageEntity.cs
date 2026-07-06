using SqlSugar;

namespace CursorUsageNotify.Models.Entities;

/// <summary>
/// 当前计费周期用量汇总快照。
/// 去重键：FetchTime（按秒截断），同一秒内多次拉取视为同一条。
/// </summary>
[SugarTable("period_usage")]
public sealed class PeriodUsageEntity
{
    /// <summary>自增主键。SQLite 要求 AUTOINCREMENT 必须是 INTEGER PRIMARY KEY，故显式指定列类型。</summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true, ColumnName = "id", ColumnDataType = "INTEGER")]
    public long Id { get; set; }

    /// <summary>本快照入库时间（epoch 毫秒，按秒截断作为去重键）。</summary>
    [SugarColumn(ColumnName = "fetch_time", IsNullable = false)]
    public long FetchTime { get; set; }

    /// <summary>计费周期开始时间（epoch 毫秒）。</summary>
    [SugarColumn(ColumnName = "period_start")]
    public long PeriodStart { get; set; }

    /// <summary>计费周期结束时间（epoch 毫秒）。</summary>
    [SugarColumn(ColumnName = "period_end")]
    public long PeriodEnd { get; set; }

    /// <summary>订阅计划名（Pro/Ultra/Plus/Hobby 等）。</summary>
    [SugarColumn(ColumnName = "plan_name", IsNullable = true, Length = 64)]
    public string? PlanName { get; set; }

    /// <summary>计划包含的请求额度。</summary>
    [SugarColumn(ColumnName = "included_requests")]
    public long IncludedRequests { get; set; }

    /// <summary>本周期已用请求数。</summary>
    [SugarColumn(ColumnName = "used_requests")]
    public long UsedRequests { get; set; }

    /// <summary>本周期已用 token 数（token 计费模式）。</summary>
    [SugarColumn(ColumnName = "used_tokens")]
    public long UsedTokens { get; set; }

    /// <summary>本周期总支出（美分）。</summary>
    [SugarColumn(ColumnName = "total_spend_cents")]
    public decimal TotalSpendCents { get; set; }

    /// <summary>本周期按需付费请求数（fast premium）。</summary>
    [SugarColumn(ColumnName = "fast_premium_requests")]
    public long FastPremiumRequests { get; set; }

    /// <summary>本周期剩余请求额度。</summary>
    [SugarColumn(ColumnName = "remaining_requests")]
    public long RemainingRequests { get; set; }

    /// <summary>原始 JSON（保留完整响应，便于后续字段扩展）。</summary>
    [SugarColumn(ColumnName = "raw_json", IsNullable = true, ColumnDataType = "TEXT")]
    public string? RawJson { get; set; }
}
