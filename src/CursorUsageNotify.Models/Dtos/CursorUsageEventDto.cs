using System.Text.Json.Serialization;

namespace CursorUsageNotify.Models.Dtos;

/// <summary>
/// 单次用量事件 DTO，对应 /api/dashboard/get-filtered-usage-events 响应数组项。
/// 字段基于社区逆向结果，个人账号响应结构可能略有差异，未识别字段进入 <see cref="ExtraFields"/>。
/// </summary>
public sealed class CursorUsageEventDto
{
    /// <summary>事件时间戳（epoch 毫秒，字符串形式）。</summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>用户邮箱。</summary>
    [JsonPropertyName("userEmail")]
    public string? UserEmail { get; set; }

    /// <summary>AI 模型名。</summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>计费类别。</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>是否使用 max 模式。</summary>
    [JsonPropertyName("maxMode")]
    public bool MaxMode { get; set; }

    /// <summary>请求成本单位。</summary>
    [JsonPropertyName("requestsCosts")]
    public decimal RequestsCosts { get; set; }

    /// <summary>是否按 token 计费。</summary>
    [JsonPropertyName("isTokenBasedCall")]
    public bool IsTokenBasedCall { get; set; }

    /// <summary>是否产生费用。</summary>
    [JsonPropertyName("isChargeable")]
    public bool IsChargeable { get; set; }

    /// <summary>是否为无客户端请求。</summary>
    [JsonPropertyName("isHeadless")]
    public bool IsHeadless { get; set; }

    /// <summary>token 用量明细。</summary>
    [JsonPropertyName("tokenUsage")]
    public TokenUsageDto? TokenUsage { get; set; }

    /// <summary>实际计费金额（美分）。</summary>
    [JsonPropertyName("chargedCents")]
    public decimal ChargedCents { get; set; }

    /// <summary>Cursor Token Fee（若启用）。</summary>
    [JsonPropertyName("cursorTokenFee")]
    public decimal CursorTokenFee { get; set; }

    /// <summary>未识别字段（保留原始数据便于后续扩展）。</summary>
    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }

    /// <summary>解析时间戳字符串为 long，失败返回 0。</summary>
    public long TryGetTimestampMs() =>
        long.TryParse(Timestamp, out var ms) ? ms : 0;
}

/// <summary>
/// token 用量明细。
/// </summary>
public sealed class TokenUsageDto
{
    /// <summary>输入 token。</summary>
    [JsonPropertyName("inputTokens")]
    public long InputTokens { get; set; }

    /// <summary>输出 token。</summary>
    [JsonPropertyName("outputTokens")]
    public long OutputTokens { get; set; }

    /// <summary>缓存读取 token。</summary>
    [JsonPropertyName("cacheReadTokens")]
    public long CacheReadTokens { get; set; }

    /// <summary>缓存写入 token。</summary>
    [JsonPropertyName("cacheWriteTokens")]
    public long CacheWriteTokens { get; set; }

    /// <summary>本次事件总费用（美分）。</summary>
    [JsonPropertyName("totalCents")]
    public decimal TotalCents { get; set; }
}
