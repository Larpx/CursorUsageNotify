using Larpx.PersonalTools.CursorUsageNotify.Models;
using Larpx.PersonalTools.CursorUsageNotify.Services.Platforms;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// DeepSeek API 调用异常基类。
    /// 继承 <see cref="PlatformApiException"/> 以便 UsageSyncHostedService 统一捕获处理。
    /// </summary>
    public class DeepSeekApiException : PlatformApiException
    {
        /// <summary>
        /// 初始化 <see cref="DeepSeekApiException"/> 实例（错误类别默认 Unknown）。
        /// </summary>
        /// <param name="message">异常消息。</param>
        /// <param name="statusCode">HTTP 状态码（网络层失败时为 0）。</param>
        /// <param name="inner">内部异常。</param>
        public DeepSeekApiException(string message, int statusCode = 0, Exception? inner = null)
            : base(PlatformType.DeepSeek, message, statusCode, PlatformApiErrorKind.Unknown, inner) { }

        /// <summary>
        /// 初始化 <see cref="DeepSeekApiException"/> 实例（指定错误类别，供子类使用）。
        /// </summary>
        /// <param name="message">异常消息。</param>
        /// <param name="statusCode">HTTP 状态码。</param>
        /// <param name="errorKind">错误类别。</param>
        /// <param name="inner">内部异常。</param>
        protected DeepSeekApiException(string message, int statusCode, PlatformApiErrorKind errorKind, Exception? inner = null)
            : base(PlatformType.DeepSeek, message, statusCode, errorKind, inner) { }
    }

    /// <summary>
    /// 认证失败（401/403 或 API 返回 code=40003），提示用户重新获取 userToken。
    /// </summary>
    public sealed class DeepSeekApiAuthException : DeepSeekApiException
    {
        /// <summary>
        /// 初始化 <see cref="DeepSeekApiAuthException"/> 实例。
        /// </summary>
        /// <param name="statusCode">HTTP 状态码（401 或 403）。</param>
        public DeepSeekApiAuthException(int statusCode)
            : base($"DeepSeek 认证失败（{statusCode}），请重新登录 platform.deepseek.com 获取 userToken。", statusCode, PlatformApiErrorKind.Auth) { }
    }

    /// <summary>
    /// 端点已变更或失效（404 Not Found / 405 Method Not Allowed）。
    /// </summary>
    public sealed class DeepSeekApiEndpointChangedException : DeepSeekApiException
    {
        /// <summary>
        /// 失效的 API 端点路径。
        /// </summary>
        public string EndpointPath { get; }

        /// <summary>
        /// 初始化 <see cref="DeepSeekApiEndpointChangedException"/> 实例。
        /// </summary>
        /// <param name="path">失效的 API 端点路径。</param>
        /// <param name="statusCode">HTTP 状态码（404 或 405）。</param>
        public DeepSeekApiEndpointChangedException(string path, int statusCode)
            : base($"DeepSeek API 端点 {path} 已变更或失效（HTTP {statusCode}），可能客户端版本过旧。", statusCode, PlatformApiErrorKind.EndpointChanged)
        {
            EndpointPath = path;
        }
    }

    /// <summary>
    /// 速率限制（429）。
    /// </summary>
    public sealed class DeepSeekApiRateLimitException : DeepSeekApiException
    {
        /// <summary>
        /// 初始化 <see cref="DeepSeekApiRateLimitException"/> 实例。
        /// </summary>
        public DeepSeekApiRateLimitException()
            : base("DeepSeek API 速率限制（429），请稍后重试。", 429, PlatformApiErrorKind.RateLimit) { }
    }
}
