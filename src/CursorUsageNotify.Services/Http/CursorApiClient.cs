using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Core.Configuration;
using Larpx.PersonalTools.CursorUsageNotify.Models.Api;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Microsoft.Extensions.Logging;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// Cursor 内部 dashboard API 客户端实现。
    /// 端点结构基于 2026-07 实际抓包：usage-events 为 POST，其余（period-usage/stripe/profile/billing-cycle/invoices/sessions）均为 GET JSON。
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

        /// <summary>
        /// 初始化 <see cref="CursorApiClient"/> 实例，配置基础地址和超时。
        /// </summary>
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
            // 实际端点为 GET（抓包验证），失败时返回空对象，不影响事件入库
            return await SendGetSafeAsync<CursorPeriodUsageDto>(sessionToken, Constants.CurrentPeriodUsagePath, ct)
                ?? new CursorPeriodUsageDto();
        }

        /// <inheritdoc/>
        public async Task<CursorStripeSubscriptionDto> GetStripeSubscriptionAsync(string sessionToken, CancellationToken ct = default)
            => await SendGetSafeAsync<CursorStripeSubscriptionDto>(sessionToken, Constants.StripeSubscriptionPath, ct)
                ?? new CursorStripeSubscriptionDto();

        /// <inheritdoc/>
        public async Task<CursorUserProfileDto> GetUserProfileAsync(string sessionToken, CancellationToken ct = default)
            => await SendGetSafeAsync<CursorUserProfileDto>(sessionToken, Constants.UserProfilePath, ct)
                ?? new CursorUserProfileDto();

        /// <inheritdoc/>
        public async Task<CursorBillingCycleDto> GetBillingCycleAsync(string sessionToken, CancellationToken ct = default)
            => await SendGetSafeAsync<CursorBillingCycleDto>(sessionToken, Constants.BillingCyclePath, ct)
                ?? new CursorBillingCycleDto();

        /// <inheritdoc/>
        public async Task<CursorInvoicesDto> ListInvoicesAsync(string sessionToken, CancellationToken ct = default)
            => await SendGetSafeAsync<CursorInvoicesDto>(sessionToken, Constants.ListInvoicesPath, ct)
                ?? new CursorInvoicesDto();

        /// <inheritdoc/>
        public async Task<CursorSessionsDto> GetSessionsAsync(string sessionToken, CancellationToken ct = default)
            => await SendGetSafeAsync<CursorSessionsDto>(sessionToken, Constants.SessionsPath, ct)
                ?? new CursorSessionsDto();

        /// <summary>
        /// POST 请求（用于 usage-events）。
        /// </summary>
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
                HandleError(response.StatusCode, responseBody, path);
            }

            var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
            if (result is null)
            {
                throw new CursorApiException($"响应反序列化失败：{typeof(T).Name}");
            }

            return result;
        }

        /// <summary>
        /// GET 请求通用方法。失败时抛 <see cref="CursorApiException"/>（含 401/403 认证异常）。
        /// </summary>
        private async Task<T> SendGetAsync<T>(string sessionToken, string path, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            ApplyHeaders(request, sessionToken);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                HandleError(response.StatusCode, responseBody, path);
            }

            var result = JsonSerializer.Deserialize<T>(responseBody, JsonOptions);
            if (result is null)
            {
                throw new CursorApiException($"响应反序列化失败：{typeof(T).Name}");
            }

            return result;
        }

        /// <summary>
        /// GET 请求容错版本：失败时记录日志并返回 null（辅助数据源，不影响事件入库主流程）。
        /// 401/403 认证异常向上抛出，由调用方决定是否终止同步。
        /// </summary>
        private async Task<T?> SendGetSafeAsync<T>(string sessionToken, string path, CancellationToken ct) where T : class
        {
            try
            {
                return await SendGetAsync<T>(sessionToken, path, ct);
            }
            catch (CursorApiAuthException)
            {
                // 认证失败向上抛，触发 cookie 失效通知
                throw;
            }
            catch (CursorApiEndpointChangedException)
            {
                // 端点已变更：向上抛，由调用方明确通知用户而非静默失败
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "GET {Path} 失败（不影响事件入库）", path);
                return null;
            }
        }

        private static void ApplyHeaders(HttpRequestMessage request, string sessionToken)
        {
            request.Headers.Add("Cookie", $"{Constants.SessionCookieName}={sessionToken}");
            request.Headers.Add("Origin", Constants.Origin);
            request.Headers.Add("Referer", Constants.Origin + "/dashboard/billing");
            request.Headers.UserAgent.ParseAdd(Constants.UserAgent);
        }

        private void HandleError(System.Net.HttpStatusCode statusCode, string body, string path)
        {
            _logger.LogError("Cursor API 调用失败：{Path} {StatusCode} {Body}", path, (int)statusCode, body);

            throw statusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    => new CursorApiAuthException((int)statusCode),
                System.Net.HttpStatusCode.BadRequest
                    => new CursorApiBadRequestException(Truncate(body, 200)),
                System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed
                    => new CursorApiEndpointChangedException(path, (int)statusCode),
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
}
