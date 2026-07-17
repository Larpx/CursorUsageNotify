using System.Text.Json;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Models.Dtos;
using Microsoft.Extensions.Logging;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// DeepSeek 开放平台 API 客户端实现。
    /// 认证：Authorization: Bearer {userToken}。
    /// 所有端点均为 GET JSON，响应为 code/msg/data 三层结构。
    /// </summary>
    public sealed class DeepSeekApiClient : IDeepSeekApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DeepSeekApiClient> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            // DeepSeek API 部分数值字段在不同版本可能返回数字或字符串数字，统一允许从字符串读取数值
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        /// <summary>
        /// 初始化 <see cref="DeepSeekApiClient"/> 实例。
        /// BaseAddress 固定为常量，防止用户误配。
        /// </summary>
        /// <param name="httpClient">由 DI 注入的 HttpClient（已配置 Polly 重试）。</param>
        /// <param name="logger">日志器。</param>
        public DeepSeekApiClient(HttpClient httpClient, ILogger<DeepSeekApiClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _httpClient.BaseAddress = new Uri(Constants.DeepSeekApiBaseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.DeepSeekUserAgent);
        }

        /// <inheritdoc/>
        public async Task<Result<int>> TestConnectionAsync(string userToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userToken))
            {
                return Result<int>.Fail("userToken 为空，请先在设置中粘贴。");
            }

            try
            {
                var summary = await GetUserSummaryAsync(userToken, ct);
                // 用 monthly_usage 作为摘要返回（非 0 表示有数据）
                return Result<int>.Ok((int)(summary.MonthlyUsage ?? 0));
            }
            catch (DeepSeekApiAuthException ex)
            {
                return Result<int>.Fail(ex.Message);
            }
            catch (DeepSeekApiException ex)
            {
                return Result<int>.Fail(ex.Message);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "DeepSeek 测试连接网络异常");
                return Result<int>.Fail($"网络异常：{ex.Message}");
            }
        }

        /// <inheritdoc/>
        public async Task<DeepSeekUserSummaryDto> GetUserSummaryAsync(string userToken, CancellationToken ct = default)
        {
            var resp = await SendGetAsync<DeepSeekUserSummaryDto>(userToken, Constants.DeepSeekUserSummaryPath, ct);
            return resp ?? throw new DeepSeekApiException("get_user_summary 返回空数据");
        }

        /// <inheritdoc/>
        public async Task<DeepSeekCurrentUserDto> GetCurrentUserAsync(string userToken, CancellationToken ct = default)
        {
            var resp = await SendGetAsync<DeepSeekCurrentUserDto>(userToken, Constants.DeepSeekCurrentUserPath, ct);
            return resp ?? throw new DeepSeekApiException("users/current 返回空数据");
        }

        /// <inheritdoc/>
        public async Task<DeepSeekUsageAmountDto> GetUsageAmountAsync(
            string userToken, long startSec, long endSec, CancellationToken ct = default)
        {
            var path = $"{Constants.DeepSeekUsageAmountPath}?start={startSec}&end={endSec}&tz=0";
            var resp = await SendGetAsync<DeepSeekUsageAmountDto>(userToken, path, ct);
            return resp ?? throw new DeepSeekApiException("usage/by_api_key/amount 返回空数据");
        }

        /// <inheritdoc/>
        public async Task<DeepSeekUsageCostDto> GetUsageCostAsync(
            string userToken, long startSec, long endSec, CancellationToken ct = default)
        {
            var path = $"{Constants.DeepSeekUsageCostPath}?start={startSec}&end={endSec}&tz=0";
            var resp = await SendGetAsync<DeepSeekUsageCostDto>(userToken, path, ct);
            return resp ?? throw new DeepSeekApiException("usage/by_api_key/cost 返回空数据");
        }

        /// <inheritdoc/>
        public async Task<DeepSeekApiKeysDto> GetApiKeysAsync(string userToken, CancellationToken ct = default)
        {
            var resp = await SendGetAsync<DeepSeekApiKeysDto>(userToken, Constants.DeepSeekApiKeysPath, ct);
            return resp ?? new DeepSeekApiKeysDto();
        }

        /// <summary>
        /// GET 请求通用方法：解析 DeepSeek 三层响应结构，认证失败/端点变更向上抛异常。
        /// </summary>
        /// <typeparam name="T">biz_data 的具体类型。</typeparam>
        /// <param name="userToken">DeepSeek userToken。</param>
        /// <param name="path">API 路径（含 query string）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>反序列化后的 biz_data，外层 code != 0 或 biz_code != 0 时抛异常。</returns>
        private async Task<T?> SendGetAsync<T>(string userToken, string path, CancellationToken ct) where T : class
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                HandleError(response.StatusCode, responseBody, path);
            }

            var wrapped = JsonSerializer.Deserialize<DeepSeekResponse<T>>(responseBody, JsonOptions);
            if (wrapped is null)
            {
                throw new DeepSeekApiException($"响应反序列化失败：{typeof(T).Name}");
            }

            // DeepSeek 外层 code=40003 表示 token 失效
            if (wrapped.Code == 40003)
            {
                throw new DeepSeekApiAuthException(200);
            }

            if (!wrapped.IsSuccess)
            {
                throw new DeepSeekApiException(
                    $"DeepSeek API 返回错误：code={wrapped.Code}, biz_code={wrapped.Data?.BizCode}, msg={wrapped.Msg}, biz_msg={wrapped.Data?.BizMsg}");
            }

            return wrapped.Data?.BizDataValue;
        }

        /// <summary>
        /// 根据 HTTP 状态码抛出对应的 DeepSeek 异常。
        /// </summary>
        private void HandleError(System.Net.HttpStatusCode statusCode, string body, string path)
        {
            _logger.LogError("DeepSeek API 调用失败：{Path} {StatusCode} {Body}", path, (int)statusCode, body);

            throw statusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                    => new DeepSeekApiAuthException((int)statusCode),
                System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed
                    => new DeepSeekApiEndpointChangedException(path, (int)statusCode),
                System.Net.HttpStatusCode.TooManyRequests
                    => new DeepSeekApiRateLimitException(),
                _ => new DeepSeekApiException(
                    $"DeepSeek API 返回 {(int)statusCode}：{Truncate(body, 200)}",
                    (int)statusCode)
            };
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max] + "...";
    }
}
