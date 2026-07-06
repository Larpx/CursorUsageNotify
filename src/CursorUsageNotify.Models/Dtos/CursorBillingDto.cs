using System.Text.Json.Serialization;

namespace CursorUsageNotify.Models.Dtos;

/// <summary>
/// Stripe 订阅信息 DTO，对应 /api/auth/stripe 响应（2026-07 实际抓包验证）。
/// </summary>
public sealed class CursorStripeSubscriptionDto
{
    /// <summary>会员类型（pro/ultra/free 等）。</summary>
    [JsonPropertyName("membershipType")]
    public string? MembershipType { get; set; }

    /// <summary>Stripe 客户 ID。</summary>
    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    /// <summary>订阅状态（active/trialing/canceled 等）。</summary>
    [JsonPropertyName("subscriptionStatus")]
    public string? SubscriptionStatus { get; set; }

    /// <summary>是否自动续费。</summary>
    [JsonPropertyName("isOnBillableAuto")]
    public bool IsOnBillableAuto { get; set; }

    /// <summary>是否年付计划。</summary>
    [JsonPropertyName("isYearlyPlan")]
    public bool IsYearlyPlan { get; set; }

    /// <summary>是否符合试用。</summary>
    [JsonPropertyName("trialEligible")]
    public bool TrialEligible { get; set; }

    /// <summary>试用天数。</summary>
    [JsonPropertyName("trialLengthDays")]
    public int TrialLengthDays { get; set; }

    /// <summary>待取消日期（ISO 字符串，null 表示未申请取消）。</summary>
    [JsonPropertyName("pendingCancellationDate")]
    public string? PendingCancellationDate { get; set; }

    /// <summary>个人会员类型。</summary>
    [JsonPropertyName("individualMembershipType")]
    public string? IndividualMembershipType { get; set; }

    /// <summary>是否团队成员。</summary>
    [JsonPropertyName("isTeamMember")]
    public bool IsTeamMember { get; set; }

    /// <summary>上次支付是否失败。</summary>
    [JsonPropertyName("lastPaymentFailed")]
    public bool LastPaymentFailed { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 用户资料 DTO，对应 /api/dashboard/get-user-profile 响应。
/// </summary>
public sealed class CursorUserProfileDto
{
    [JsonPropertyName("profile")]
    public CursorUserProfileData? Profile { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

public sealed class CursorUserProfileData
{
    /// <summary>用户名 handle（唯一标识）。</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>显示名。</summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>头像 URL。</summary>
    [JsonPropertyName("avatarUrl")]
    public string? AvatarUrl { get; set; }

    /// <summary>账号创建时间（ISO 字符串）。</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 当前计费周期 DTO，对应 /api/dashboard/get-current-billing-cycle 响应。
/// 时间戳为字符串 epoch 毫秒。
/// </summary>
public sealed class CursorBillingCycleDto
{
    [JsonPropertyName("startDateEpochMillis")]
    public string? StartDateEpochMillis { get; set; }

    [JsonPropertyName("endDateEpochMillis")]
    public string? EndDateEpochMillis { get; set; }

    [JsonIgnore]
    public long StartMs => long.TryParse(StartDateEpochMillis, out var v) ? v : 0;

    [JsonIgnore]
    public long EndMs => long.TryParse(EndDateEpochMillis, out var v) ? v : 0;

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 发票列表 DTO，对应 /api/dashboard/list-invoices 响应。
/// </summary>
public sealed class CursorInvoicesDto
{
    [JsonPropertyName("invoices")]
    public List<CursorInvoiceDto>? Invoices { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("hasMore")]
    public bool HasMore { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 发票 DTO（字段名基于实际抓包：invoiceId/date/amountCents/status/hostedInvoiceUrl）。
/// </summary>
public sealed class CursorInvoiceDto
{
    /// <summary>发票 ID。</summary>
    [JsonPropertyName("invoiceId")]
    public string? InvoiceId { get; set; }

    /// <summary>发票日期（字符串 epoch 毫秒）。</summary>
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>金额（美分）。</summary>
    [JsonPropertyName("amountCents")]
    public long AmountCents { get; set; }

    /// <summary>货币（usd）。</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>状态（paid/open/void 等）。</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Stripe 发票页面 URL。</summary>
    [JsonPropertyName("hostedInvoiceUrl")]
    public string? HostedInvoiceUrl { get; set; }

    [JsonIgnore]
    public long DateMs => long.TryParse(Date, out var v) ? v : 0;

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}

/// <summary>
/// 会话列表 DTO，对应 /api/auth/sessions 响应。用于检测 cookie/session 有效期。
/// </summary>
public sealed class CursorSessionsDto
{
    [JsonPropertyName("sessions")]
    public List<CursorSessionDto>? Sessions { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }

    /// <summary>最近一次 WEB 会话的过期时间（UTC），无会话返回 null。</summary>
    [JsonIgnore]
    public DateTime? LatestWebSessionExpiryUtc
    {
        get
        {
            if (Sessions is null || Sessions.Count == 0) return null;
            return Sessions
                .Where(s => s.Type == "SESSION_TYPE_WEB" && !string.IsNullOrEmpty(s.ExpiresAt))
                .Select(s => DateTime.TryParse(s.ExpiresAt, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt) ? dt : (DateTime?)null)
                .Where(d => d.HasValue)
                .Max(d => d!.Value);
        }
    }
}

public sealed class CursorSessionDto
{
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? ExtraFields { get; set; }
}
