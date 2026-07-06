using System.Text.Json.Serialization;

namespace CursorUsageNotify.Models.Dtos;

/// <summary>
/// Cursor 账单页（/dashboard/billing）Next.js __NEXT_DATA__ 根结构。
/// </summary>
public sealed class CursorBillingPageData
{
    [JsonPropertyName("props")]
    public CursorBillingPageProps? Props { get; set; }

    [JsonPropertyName("page")]
    public string? Page { get; set; }
}

/// <summary>
/// Next.js pageProps 包装。
/// </summary>
public sealed class CursorBillingPageProps
{
    [JsonPropertyName("pageProps")]
    public CursorBillingPagePropsData? PageProps { get; set; }
}

/// <summary>
/// 账单页实际数据。
/// </summary>
public sealed class CursorBillingPagePropsData
{
    /// <summary>用户/团队信息。</summary>
    [JsonPropertyName("team")]
    public CursorTeamDto? Team { get; set; }

    /// <summary>订阅信息。</summary>
    [JsonPropertyName("subscription")]
    public CursorSubscriptionDto? Subscription { get; set; }

    /// <summary>账单历史列表。</summary>
    [JsonPropertyName("invoices")]
    public List<CursorInvoiceDto>? Invoices { get; set; }

    /// <summary>用量摘要。</summary>
    [JsonPropertyName("usageSummary")]
    public object? UsageSummary { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 用户/团队信息 DTO。
/// </summary>
public sealed class CursorTeamDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 订阅信息 DTO。
/// </summary>
public sealed class CursorSubscriptionDto
{
    /// <summary>订阅计划（Pro/Ultra 等）。</summary>
    [JsonPropertyName("plan")]
    public string? Plan { get; set; }

    /// <summary>订阅状态（active/trialing/canceled 等）。</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>当前周期开始（epoch 毫秒）。</summary>
    [JsonPropertyName("currentPeriodStart")]
    public long? CurrentPeriodStart { get; set; }

    /// <summary>当前周期结束（epoch 毫秒）。</summary>
    [JsonPropertyName("currentPeriodEnd")]
    public long? CurrentPeriodEnd { get; set; }

    /// <summary>试用结束时间（epoch 毫秒，可能为 null）。</summary>
    [JsonPropertyName("trialEnd")]
    public long? TrialEnd { get; set; }

    /// <summary>是否在周期结束时取消订阅。</summary>
    [JsonPropertyName("cancelAtPeriodEnd")]
    public bool CancelAtPeriodEnd { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 账单/发票 DTO。
/// </summary>
public sealed class CursorInvoiceDto
{
    /// <summary>发票 ID。</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>发票日期（epoch 毫秒）。</summary>
    [JsonPropertyName("date")]
    public long? Date { get; set; }

    /// <summary>金额（美分）。</summary>
    [JsonPropertyName("amount")]
    public long? Amount { get; set; }

    /// <summary>货币（usd）。</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>状态（paid/open/void 等）。</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>账单对应周期开始（epoch 毫秒）。</summary>
    [JsonPropertyName("periodStart")]
    public long? PeriodStart { get; set; }

    /// <summary>账单对应周期结束（epoch 毫秒）。</summary>
    [JsonPropertyName("periodEnd")]
    public long? PeriodEnd { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}