
namespace Larpx.PersonalTools.CursorUsageNotify.Core
{
    /// <summary>
    /// 全局常量。集中管理避免魔法字符串。
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Cursor 内部 dashboard 接口：详细用量事件（model、token、cost）。
        /// </summary>
        public const string FilteredUsageEventsPath = "/api/dashboard/get-filtered-usage-events";

        /// <summary>
        /// Cursor 内部 dashboard 接口：当前计费周期汇总。
        /// </summary>
        public const string CurrentPeriodUsagePath = "/api/dashboard/get-current-period-usage";

        /// <summary>
        /// Stripe 订阅信息（计划/状态/自动续费等）。GET。
        /// </summary>
        public const string StripeSubscriptionPath = "/api/auth/stripe";

        /// <summary>
        /// 用户资料（handle/displayName/avatar/createdAt）。GET。
        /// </summary>
        public const string UserProfilePath = "/api/dashboard/get-user-profile";

        /// <summary>
        /// 当前计费周期起止时间。GET。
        /// </summary>
        public const string BillingCyclePath = "/api/dashboard/get-current-billing-cycle";

        /// <summary>
        /// 发票列表。GET。
        /// </summary>
        public const string ListInvoicesPath = "/api/dashboard/list-invoices";

        /// <summary>
        /// 会话列表（用于检测 cookie/session 过期时间）。GET。
        /// </summary>
        public const string SessionsPath = "/api/auth/sessions";

        /// <summary>
        /// 用于认证的 Cookie 名称（用户从浏览器 F12 复制）。
        /// </summary>
        public const string SessionCookieName = "WorkosCursorSessionToken";

        /// <summary>
        /// 请求来源，固定为 cursor.com 避免被服务端拒绝。
        /// </summary>
        public const string Origin = "https://cursor.com";

        /// <summary>
        /// 模拟浏览器 User-Agent，避免被反爬拒绝。
        /// </summary>
        public const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        /// <summary>
        /// Application 云消息标识符（Win10/11 Toast 通知需要）。
        /// </summary>
        public const string ToastAppId = "CursorUsageNotify.App";

        /// <summary>
        /// Toast 显示名称。
        /// </summary>
        public const string ToastDisplayName = "Cursor 用量通知";

        // ──────────────────────────────────────────────────────────────
        // DeepSeek 开放平台 API 常量
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// DeepSeek 开放平台基础地址（不可配置，防止用户误配）。
        /// </summary>
        public const string DeepSeekApiBaseUrl = "https://platform.deepseek.com";

        /// <summary>
        /// DeepSeek 用户账户汇总端点（余额、本月用量、本月费用）。
        /// </summary>
        public const string DeepSeekUserSummaryPath = "/api/v0/users/get_user_summary";

        /// <summary>
        /// DeepSeek 按 API Key 分组的 token 用量端点（每日桶）。
        /// 参数：start/end（Unix 秒）、tz=0。
        /// </summary>
        public const string DeepSeekUsageAmountPath = "/api/v0/usage/by_api_key/amount";

        /// <summary>
        /// DeepSeek 按 API Key 分组的费用端点（每日桶）。
        /// 参数：start/end（Unix 秒）、tz=0。
        /// </summary>
        public const string DeepSeekUsageCostPath = "/api/v0/usage/by_api_key/cost";

        /// <summary>
        /// DeepSeek API Key 列表端点（账户下可配置多个 Key，各自独立统计用量）。
        /// </summary>
        public const string DeepSeekApiKeysPath = "/api/v0/users/get_api_keys";

        /// <summary>
        /// DeepSeek 当前登录用户信息端点（邮箱、手机号、头像/名称等）。
        /// </summary>
        public const string DeepSeekCurrentUserPath = "/auth-api/v0/users/current";

        /// <summary>
        /// DeepSeek API 请求 User-Agent（模拟 Edge 浏览器）。
        /// </summary>
        public const string DeepSeekUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36 Edg/150.0.0.0";
    }
}
