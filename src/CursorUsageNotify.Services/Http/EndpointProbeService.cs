using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;


namespace Larpx.PersonalTools.CursorUsageNotify.Services.Http
{
    /// <summary>
    /// 启动端点探测服务：Host 启动后逐一探测 7 个 Cursor 内部 API 端点是否仍存在。
    /// 用假 token 触发 401/400 即视为端点存在；404/405 视为端点已变更。
    /// 失效端点通过 <see cref="EndpointDegradedMessage"/> 通知 UI，而非静默失败。
    /// 探测在后台异步执行，不阻塞启动。
    /// </summary>
    public sealed class EndpointProbeService : IHostedService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMessenger _messenger;
        private readonly ILogger<EndpointProbeService> _logger;
        private CancellationTokenSource? _cts;

        // 待探测端点：(路径, HTTP 方法)。usage-events 为 POST，其余为 GET。
        private static readonly (string Path, HttpMethod Method)[] Endpoints =
        {
            (Constants.FilteredUsageEventsPath, HttpMethod.Post),
            (Constants.CurrentPeriodUsagePath, HttpMethod.Get),
            (Constants.StripeSubscriptionPath, HttpMethod.Get),
            (Constants.UserProfilePath, HttpMethod.Get),
            (Constants.BillingCyclePath, HttpMethod.Get),
            (Constants.ListInvoicesPath, HttpMethod.Get),
            (Constants.SessionsPath, HttpMethod.Get),
        };

        // 探测用假 token，仅用于触发服务端校验以判断端点存在性
        private const string ProbeToken = "probe-token";

        /// <summary>
        /// 端点可用性映射：path → 是否可用。
        /// </summary>
        public IReadOnlyDictionary<string, bool> Status { get; } = new Dictionary<string, bool>();

        /// <summary>
        /// 初始化 <see cref="EndpointProbeService"/> 实例。
        /// </summary>
        public EndpointProbeService(
            IHttpClientFactory httpClientFactory,
            IMessenger messenger,
            ILogger<EndpointProbeService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _messenger = messenger;
            _logger = logger;
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();
            // 后台异步探测，不阻塞 Host 启动
            _ = Task.Run(() => ProbeAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 执行端点探测：逐一请求 7 个端点，记录可用性，失效端点发送通知。
        /// </summary>
        public async Task ProbeAsync(CancellationToken ct)
        {
            // 复用 CursorApiClient 注册的命名 HttpClient（含 BaseAddress/Timeout/Polly 配置）
            var client = _httpClientFactory.CreateClient(typeof(CursorApiClient).FullName ?? nameof(CursorApiClient));
            var status = (Dictionary<string, bool>)Status;

            foreach (var (path, method) in Endpoints)
            {
                if (ct.IsCancellationRequested)
                {
                    return;
                }

                var available = await ProbeEndpointAsync(client, path, method, ct);
                status[path] = available;
                if (!available)
                {
                    _logger.LogWarning("端点探测：{Path} 已变更或失效，请关注客户端更新", path);
                }
                else
                {
                    _logger.LogDebug("端点探测：{Path} 可用", path);
                }
            }

            var degraded = status.Where(kv => !kv.Value).Select(kv => kv.Key).ToList();
            if (degraded.Count > 0)
            {
                _messenger.Send(new EndpointDegradedMessage(degraded));
            }

            _logger.LogInformation(
                "端点探测完成：{Available}/{Total} 可用",
                status.Count(kv => kv.Value),
                status.Count);
        }

        /// <summary>
        /// 探测单个端点：404/405 视为已变更，其他状态码（含 401/400）视为端点存在。
        /// </summary>
        private async Task<bool> ProbeEndpointAsync(
            HttpClient client,
            string path,
            HttpMethod method,
            CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Add("Cookie", $"{Constants.SessionCookieName}={ProbeToken}");
                request.Headers.Add("Origin", Constants.Origin);
                request.Headers.Add("Referer", Constants.Origin + "/dashboard/billing");
                request.Headers.UserAgent.ParseAdd(Constants.UserAgent);
                request.Headers.Accept.ParseAdd("application/json");

                if (method == HttpMethod.Post)
                {
                    request.Content = new StringContent("{}", Encoding.UTF8);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                // 404/405 = 端点不存在或方法错误；其他（401/400/200/429）均表示端点存在
                return response.StatusCode != System.Net.HttpStatusCode.NotFound
                       && response.StatusCode != System.Net.HttpStatusCode.MethodNotAllowed;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                // 网络错误等无法判断，保守视为不可用
                _logger.LogDebug(ex, "端点探测异常：{Path}", path);
                return false;
            }
        }
    }
}
