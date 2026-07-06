using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CursorUsageNotify.Core;
using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.Models.Api;
using CursorUsageNotify.Models.Dtos;
using Microsoft.Extensions.Logging;

namespace CursorUsageNotify.Services.Http;

/// <summary>
/// Cursor 内部 dashboard API 客户端实现。
/// 调用 /api/dashboard/get-filtered-usage-events 和 /api/dashboard/get-current-period-usage。
/// </summary>
public sealed class CursorApiClient : ICursorApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AppSettings _settings;
    private readonly ILogger<CursorApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public CursorApiClient(HttpClient httpClient, AppSettings settings, ILogger<CursorApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(settings.ApiBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(settings.HttpTimeoutSeconds);
    }

    /// <inheritdoc/>
    public async Task<Result<int>> TestConnectionAsync(string sessionToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return Result<int>.Fail("Session token 为空，请先在设置中粘贴。");
        }

        try
        {
            var resp = await GetFilteredUsageEventsAsync(sessionToken, page: 1, pageSize: 1, startTimestamp: 0, ct);
            return Result<int>.Ok(resp.TotalUsageEventsCount);
        }
        catch (CursorApiAuthException ex)
        {
            return Result<int>.Fail(ex.Message);
        }
        catch (CursorApiBadRequestException ex)
        {
            return Result<int>.Fail(ex.Message);
        }
        catch (CursorApiException ex)
        {
            return Result<int>.Fail(ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "测试连接网络异常");
            return Result<int>.Fail($"网络异常：{ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<CursorUsageEventsResponse> GetFilteredUsageEventsAsync(
        string sessionToken,
        int page = 1,
        int pageSize = 500,
        long startTimestamp = 0,
        CancellationToken ct = default)
    {
        var body = new { page, pageSize, startDate = startTimestamp };
        return await SendAsync<CursorUsageEventsResponse>(
            sessionToken,
            Constants.FilteredUsageEventsPath,
            body,
            ct);
    }

    /// <inheritdoc/>
    public async Task<CursorPeriodUsageDto> GetCurrentPeriodUsageAsync(string sessionToken, CancellationToken ct = default)
    {
        return await SendAsync<CursorPeriodUsageDto>(
            sessionToken,
            Constants.CurrentPeriodUsagePath,
            new { },
            ct);
    }

    private async Task<T> SendAsync<T>(string sessionToken, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        ApplyHeaders(request, sessionToken);

        var json = JsonSerializer.Serialize(body, JsonOptions);
        request.Content = new StringContent(json, Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            HandleError(response.StatusCode, responseBody);
        }

        var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
        if (result is null)
        {
            throw new CursorApiException($"响应反序列化失败：{typeof(T).Name}");
        }

        return result;
    }

    private static void ApplyHeaders(HttpRequestMessage request, string sessionToken)
    {
        request.Headers.Add("Cookie", $"{Constants.SessionCookieName}={sessionToken}");
        request.Headers.Add("Origin", Constants.Origin);
        request.Headers.Add("Referer", Constants.Origin + "/dashboard/spending");
        request.Headers.UserAgent.ParseAdd(Constants.UserAgent);
    }

    private void HandleError(System.Net.HttpStatusCode statusCode, string body)
    {
        _logger.LogError("Cursor API 调用失败：{StatusCode} {Body}", (int)statusCode, body);

        throw statusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                => new CursorApiAuthException((int)statusCode),
            System.Net.HttpStatusCode.BadRequest
                => new CursorApiBadRequestException(Truncate(body, 200)),
            System.Net.HttpStatusCode.TooManyRequests
                => new CursorApiRateLimitException(),
            _ => new CursorApiException(
                $"Cursor API 返回 {(int)statusCode}：{Truncate(body, 200)}",
                (int)statusCode)
        };
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
