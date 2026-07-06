using System.Text.Json.Serialization;

namespace CursorUsageNotify.Models.Dtos;

/// <summary>
/// 当前计费周期用量汇总 DTO，对应 /api/dashboard/get-current-period-usage 响应。
/// 个人账号响应结构无官方文档，关键字段尽量宽松映射，未识别字段保留在 <see cref="ExtraFields"/>。
/// </summary>
public sealed class CursorPeriodUsageDto
{
    /// <summary>计费周期开始时间（epoch 毫秒）。</summary>
    [JsonPropertyName("periodStart")]
    public long? PeriodStart { get; set; }

    /// <summary>计费周期结束时间（epoch 毫秒）。</summary>
    [JsonPropertyName("periodEnd")]
    public long? PeriodEnd { get; set; }

    /// <summary>订阅计划名。</summary>
    [JsonPropertyName("planName")]
    public string? PlanName { get; set; }

    /// <summary>计划包含的请求额度。</summary>
    [JsonPropertyName("includedRequests")]
    public long? IncludedRequests { get; set; }

    /// <summary>已用请求数。</summary>
    [JsonPropertyName("usedRequests")]
    public long? UsedRequests { get; set; }

    /// <summary>已用 token 数。</summary>
    [JsonPropertyName("usedTokens")]
    public long? UsedTokens { get; set; }

    /// <summary>本周期总支出（美分）。</summary>
    [JsonPropertyName("totalSpendCents")]
    public decimal? TotalSpendCents { get; set; }

    /// <summary>按需付费请求数。</summary>
    [JsonPropertyName("fastPremiumRequests")]
    public long? FastPremiumRequests { get; set; }

    /// <summary>剩余请求额度。</summary>
    [JsonPropertyName("remainingRequests")]
    public long? RemainingRequests { get; set; }

    /// <summary>用户邮箱（部分响应可能携带）。</summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>未识别字段。</summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}
