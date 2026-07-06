namespace CursorUsageNotify.Core;

/// <summary>
/// 全局常量。集中管理避免魔法字符串。
/// </summary>
public static class Constants
{
    /// <summary>Cursor 内部 dashboard 接口：详细用量事件（model、token、cost）。</summary>
    public const string FilteredUsageEventsPath = "/api/dashboard/get-filtered-usage-events";

    /// <summary>Cursor 内部 dashboard 接口：当前计费周期汇总。</summary>
    public const string CurrentPeriodUsagePath = "/api/dashboard/get-current-period-usage";

    /// <summary>订阅/团队信息 API 端点（多个备选）。</summary>
    public static readonly string[] TeamApiEndpoints =
    {
        "/api/dashboard/get-team",
        "/api/dashboard/subscription",
        "/api/team",
        "/api/user",
        "/api/organization"
    };

    /// <summary>用于认证的 Cookie 名称（用户从浏览器 F12 复制）。</summary>
    public const string SessionCookieName = "WorkosCursorSessionToken";

    /// <summary>请求来源，固定为 cursor.com 避免被服务端拒绝。</summary>
    public const string Origin = "https://cursor.com";

    /// <summary>模拟浏览器 User-Agent，避免被反爬拒绝。</summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>Application 云消息标识符（Win10/11 Toast 通知需要）。</summary>
    public const string ToastAppId = "CursorUsageNotify.App";

    /// <summary>Toast 显示名称。</summary>
    public const string ToastDisplayName = "Cursor 用量通知";
}
