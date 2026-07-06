namespace CursorUsageNotify.Services.Http;

/// <summary>
/// Cursor API 调用异常基类。
/// </summary>
public class CursorApiException : Exception
{
    /// <summary>HTTP 状态码（网络层失败时为 0）。</summary>
    public int StatusCode { get; }

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
    public CursorApiAuthException(int statusCode)
        : base($"认证失败（{statusCode}），请重新登录 cursor.com 复制 WorkosCursorSessionToken。", statusCode) { }
}

/// <summary>
/// 请求格式错误（400），通常是 token 过期或 URL 被本地化。
/// </summary>
public sealed class CursorApiBadRequestException : CursorApiException
{
    public CursorApiBadRequestException(string detail)
        : base($"请求被拒绝（400）：{detail}。可能 token 已过期或请求路径错误。", 400) { }
}

/// <summary>
/// 速率限制（429）。
/// </summary>
public sealed class CursorApiRateLimitException : CursorApiException
{
    public CursorApiRateLimitException()
        : base("Cursor API 速率限制（429），请稍后重试。", 429) { }
}
