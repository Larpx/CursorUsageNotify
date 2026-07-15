using Larpx.PersonalTools.CursorUsageNotify.Models;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Platforms
{
    /// <summary>
    /// 平台 API 错误类别，用于 UsageSyncHostedService 统一判断处理策略。
    /// </summary>
    public enum PlatformApiErrorKind
    {
        /// <summary>
        /// 未知错误（默认）。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 认证失败（401/403），需提示用户更新 token。
        /// </summary>
        Auth = 1,

        /// <summary>
        /// 端点已变更或失效（404/405），需提示用户更新客户端。
        /// </summary>
        EndpointChanged = 2,

        /// <summary>
        /// 速率限制（429）。
        /// </summary>
        RateLimit = 3,

        /// <summary>
        /// 请求格式错误（400）。
        /// </summary>
        BadRequest = 4
    }

    /// <summary>
    /// 平台 API 调用异常基类，所有平台特定异常应继承自此。
    /// UsageSyncHostedService 统一捕获此基类处理跨平台错误。
    /// </summary>
    public class PlatformApiException : Exception
    {
        /// <summary>
        /// 发生异常的平台。
        /// </summary>
        public PlatformType Platform { get; }

        /// <summary>
        /// HTTP 状态码（网络层失败时为 0）。
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// 错误类别。
        /// </summary>
        public PlatformApiErrorKind ErrorKind { get; }

        /// <summary>
        /// 是否为认证错误（便捷判断）。
        /// </summary>
        public bool IsAuthError => ErrorKind == PlatformApiErrorKind.Auth;

        /// <summary>
        /// 是否为端点变更错误。
        /// </summary>
        public bool IsEndpointChanged => ErrorKind == PlatformApiErrorKind.EndpointChanged;

        /// <summary>
        /// 初始化 <see cref="PlatformApiException"/> 实例。
        /// </summary>
        /// <param name="platform">发生异常的平台。</param>
        /// <param name="message">异常消息。</param>
        /// <param name="statusCode">HTTP 状态码（网络层失败时为 0）。</param>
        /// <param name="errorKind">错误类别。</param>
        /// <param name="inner">内部异常。</param>
        public PlatformApiException(
            PlatformType platform,
            string message,
            int statusCode = 0,
            PlatformApiErrorKind errorKind = PlatformApiErrorKind.Unknown,
            Exception? inner = null)
            : base(message, inner)
        {
            Platform = platform;
            StatusCode = statusCode;
            ErrorKind = errorKind;
        }
    }
}
