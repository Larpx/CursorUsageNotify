using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Services.Platforms;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// Cursor API 调用异常基类。
    /// 继承 <see cref="PlatformApiException"/> 以便 UsageSyncHostedService 统一捕获处理。
    /// </summary>
    public class CursorApiException : PlatformApiException
    {
        /// <summary>
        /// 初始化 <see cref="CursorApiException"/> 实例（错误类别默认 Unknown）。
        /// </summary>
        /// <param name="message">异常消息。</param>
        /// <param name="statusCode">HTTP 状态码（网络层失败时为 0）。</param>
        /// <param name="inner">内部异常。</param>
        public CursorApiException(string message, int statusCode = 0, Exception? inner = null)
            : base(PlatformType.Cursor, message, statusCode, PlatformApiErrorKind.Unknown, inner) { }

        /// <summary>
        /// 初始化 <see cref="CursorApiException"/> 实例（指定错误类别，供子类使用）。
        /// </summary>
        /// <param name="message">异常消息。</param>
        /// <param name="statusCode">HTTP 状态码。</param>
        /// <param name="errorKind">错误类别。</param>
        /// <param name="inner">内部异常。</param>
        protected CursorApiException(string message, int statusCode, PlatformApiErrorKind errorKind, Exception? inner = null)
            : base(PlatformType.Cursor, message, statusCode, errorKind, inner) { }
    }

    /// <summary>
    /// 认证失败（401/403），提示用户重新登录获取新 token。
    /// </summary>
    public sealed class CursorApiAuthException : CursorApiException
    {
        /// <summary>
        /// 初始化 <see cref="CursorApiAuthException"/> 实例。
        /// </summary>
        /// <param name="statusCode">HTTP 状态码（401 或 403）。</param>
        public CursorApiAuthException(int statusCode)
            : base($"认证失败（{statusCode}），请重新登录 cursor.com 复制 WorkosCursorSessionToken。", statusCode, PlatformApiErrorKind.Auth) { }
    }

    /// <summary>
    /// 请求格式错误（400），通常是 token 过期或 URL 被本地化。
    /// </summary>
    public sealed class CursorApiBadRequestException : CursorApiException
    {
        /// <summary>
        /// 初始化 <see cref="CursorApiBadRequestException"/> 实例。
        /// </summary>
        /// <param name="detail">错误详情（来自响应体）。</param>
        public CursorApiBadRequestException(string detail)
            : base($"请求被拒绝（400）：{detail}。可能 token 已过期或请求路径错误。", 400, PlatformApiErrorKind.BadRequest) { }
    }

    /// <summary>
    /// 速率限制（429）。
    /// </summary>
    public sealed class CursorApiRateLimitException : CursorApiException
    {
        /// <summary>
        /// 初始化 <see cref="CursorApiRateLimitException"/> 实例。
        /// </summary>
        public CursorApiRateLimitException()
            : base("Cursor API 速率限制（429），请稍后重试。", 429, PlatformApiErrorKind.RateLimit) { }
    }

    /// <summary>
    /// 端点已变更或失效（404 Not Found / 405 Method Not Allowed）。
    /// 表示 Cursor 接口版本已更新，当前客户端调用的端点不存在或方法错误，
    /// 不应静默失败，需明确提示用户更新客户端。
    /// </summary>
    public sealed class CursorApiEndpointChangedException : CursorApiException
    {
        /// <summary>
        /// 失效的 API 端点路径。
        /// </summary>
        public string EndpointPath { get; }

        /// <summary>
        /// 初始化 <see cref="CursorApiEndpointChangedException"/> 实例。
        /// </summary>
        /// <param name="path">失效的 API 端点路径。</param>
        /// <param name="statusCode">HTTP 状态码（404 或 405）。</param>
        public CursorApiEndpointChangedException(string path, int statusCode)
            : base($"Cursor API 端点 {path} 已变更或失效（HTTP {statusCode}），可能客户端版本过旧，请检查更新。", statusCode, PlatformApiErrorKind.EndpointChanged)
        {
            EndpointPath = path;
        }
    }
}
