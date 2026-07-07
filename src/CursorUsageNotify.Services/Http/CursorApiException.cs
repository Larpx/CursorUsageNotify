
namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// Cursor API 调用异常基类。
    /// </summary>
    public class CursorApiException : Exception
    {
        /// <summary>
        /// HTTP 状态码（网络层失败时为 0）。
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// 初始化 <see cref="CursorApiException"/> 实例。
        /// </summary>
        /// <param name="message">
        /// 异常消息。
        /// </param>
        /// <param name="statusCode">
        /// HTTP 状态码（网络层失败时为 0）。
        /// </param>
        /// <param name="inner">
        /// 内部异常。
        /// </param>
        public CursorApiException(string message, int statusCode = 0, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
        }
    }

    /// <summary>
    /// 认证失败（401/403），提示用户重新登录获取新 token。
    /// </summary>
    public sealed class CursorApiAuthException : CursorApiException
    {
        /// <summary>
        /// 初始化 <see cref="CursorApiAuthException"/> 实例。
        /// </summary>
        /// <param name="statusCode">
        /// HTTP 状态码（401 或 403）。
        /// </param>
        public CursorApiAuthException(int statusCode)
            : base($"认证失败（{statusCode}），请重新登录 cursor.com 复制 WorkosCursorSessionToken。", statusCode) { }
    }

    /// <summary>
    /// 请求格式错误（400），通常是 token 过期或 URL 被本地化。
    /// </summary>
    public sealed class CursorApiBadRequestException : CursorApiException
    {
        /// <summary>
        /// 初始化 <see cref="CursorApiBadRequestException"/> 实例。
        /// </summary>
        /// <param name="detail">
        /// 错误详情（来自响应体）。
        /// </param>
        public CursorApiBadRequestException(string detail)
            : base($"请求被拒绝（400）：{detail}。可能 token 已过期或请求路径错误。", 400) { }
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
            : base("Cursor API 速率限制（429），请稍后重试。", 429) { }
    }
}
