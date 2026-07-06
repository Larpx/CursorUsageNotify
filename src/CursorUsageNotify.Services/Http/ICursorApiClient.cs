using CursorUsageNotify.Core;
using CursorUsageNotify.Models.Api;
using CursorUsageNotify.Models.Dtos;

namespace CursorUsageNotify.Services.Http;

/// <summary>
/// Cursor 内部 dashboard API 客户端抽象。
/// 端点结构基于 2026-07 实际抓包验证：除 usage-events 为 POST 外，其余均为 GET JSON API。
/// </summary>
public interface ICursorApiClient
{
    /// <summary>测试连接是否可用（拉取 1 条事件验证 token 有效性）。</summary>
    Task<Result<int>> TestConnectionAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>分页拉取详细用量事件（POST /api/dashboard/get-filtered-usage-events）。</summary>
    Task<CursorUsageEventsResponse> GetFilteredUsageEventsAsync(
        string sessionToken,
        int page = 1,
        int pageSize = 500,
        long startTimestamp = 0,
        CancellationToken ct = default);

    /// <summary>当前计费周期用量汇总（GET /api/dashboard/get-current-period-usage）。</summary>
    Task<CursorPeriodUsageDto> GetCurrentPeriodUsageAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>Stripe 订阅信息（GET /api/auth/stripe）：计划、状态、自动续费。</summary>
    Task<CursorStripeSubscriptionDto> GetStripeSubscriptionAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>用户资料（GET /api/dashboard/get-user-profile）：handle、displayName、createdAt。</summary>
    Task<CursorUserProfileDto> GetUserProfileAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>当前计费周期起止（GET /api/dashboard/get-current-billing-cycle）。</summary>
    Task<CursorBillingCycleDto> GetBillingCycleAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>发票列表（GET /api/dashboard/list-invoices）。</summary>
    Task<CursorInvoicesDto> ListInvoicesAsync(string sessionToken, CancellationToken ct = default);

    /// <summary>会话列表（GET /api/auth/sessions），用于检测 cookie 过期时间。</summary>
    Task<CursorSessionsDto> GetSessionsAsync(string sessionToken, CancellationToken ct = default);
}
