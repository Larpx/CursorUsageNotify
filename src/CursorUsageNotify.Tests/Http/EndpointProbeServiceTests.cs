using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Larpx.PersonalTools.CursorUsageNotify.Core;
using Larpx.PersonalTools.CursorUsageNotify.Services.Http;
using Larpx.PersonalTools.CursorUsageNotify.Services.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;


namespace Larpx.PersonalTools.CursorUsageNotify.Tests.Http
{
    /// <summary>
    /// EndpointProbeService 单元测试：用本地 StubHandler 模拟 HTTP 响应，
    /// 用 StrongReferenceMessenger 捕获 EndpointDegradedMessage。
    /// </summary>
    public class EndpointProbeServiceTests
    {
        private static readonly string UsageEventsPath = Constants.FilteredUsageEventsPath;
        private static readonly string PeriodPath = Constants.CurrentPeriodUsagePath;

        [Fact]
        public async Task ProbeAsync_AllEndpointsReturn200_StatusAllTrue()
        {
            var (service, messenger, recipient) = CreateService(_ => HttpStatusCode.OK);
            List<string>? degraded = null;
            messenger.Register<EndpointDegradedMessage>(recipient, (_, m) => degraded = new List<string>(m.DegradedPaths));

            await service.ProbeAsync(CancellationToken.None);

            Assert.All(service.Status.Values, available => Assert.True(available));
            Assert.Null(degraded);
        }

        [Fact]
        public async Task ProbeAsync_OneEndpointReturns404_DegradedPathRecordedAndMessageSent()
        {
            var (service, messenger, recipient) = CreateService(path =>
                path == PeriodPath ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            List<string>? degraded = null;
            messenger.Register<EndpointDegradedMessage>(recipient, (_, m) => degraded = new List<string>(m.DegradedPaths));

            await service.ProbeAsync(CancellationToken.None);

            Assert.False(service.Status[PeriodPath]);
            Assert.NotNull(degraded);
            Assert.Single(degraded!);
            Assert.Contains(PeriodPath, degraded!);
        }

        [Fact]
        public async Task ProbeAsync_EndpointReturns401_StatusTrue()
        {
            var (service, messenger, recipient) = CreateService(_ => HttpStatusCode.Unauthorized);
            List<string>? degraded = null;
            messenger.Register<EndpointDegradedMessage>(recipient, (_, m) => degraded = new List<string>(m.DegradedPaths));

            await service.ProbeAsync(CancellationToken.None);

            Assert.All(service.Status.Values, available => Assert.True(available));
            Assert.Null(degraded);
        }

        [Fact]
        public async Task ProbeAsync_EndpointReturns405_StatusFalse()
        {
            var (service, messenger, recipient) = CreateService(path =>
                path == UsageEventsPath ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK);
            List<string>? degraded = null;
            messenger.Register<EndpointDegradedMessage>(recipient, (_, m) => degraded = new List<string>(m.DegradedPaths));

            await service.ProbeAsync(CancellationToken.None);

            Assert.False(service.Status[UsageEventsPath]);
            Assert.NotNull(degraded);
            Assert.Contains(UsageEventsPath, degraded!);
        }

        [Fact]
        public async Task ProbeAsync_AllEndpointsReturn404_AllFalseAndMessageSent()
        {
            var (service, messenger, recipient) = CreateService(_ => HttpStatusCode.NotFound);
            List<string>? degraded = null;
            messenger.Register<EndpointDegradedMessage>(recipient, (_, m) => degraded = new List<string>(m.DegradedPaths));

            await service.ProbeAsync(CancellationToken.None);

            Assert.All(service.Status.Values, available => Assert.False(available));
            Assert.NotNull(degraded);
            Assert.Equal(service.Status.Count, degraded!.Count);
        }

        [Fact]
        public async Task ProbeAsync_ProbesAllSevenEndpoints()
        {
            var (service, _, _) = CreateService(_ => HttpStatusCode.OK);

            await service.ProbeAsync(CancellationToken.None);

            Assert.Equal(7, service.Status.Count);
        }

        private static (EndpointProbeService service, StrongReferenceMessenger messenger, object recipient)
            CreateService(Func<string, HttpStatusCode> statusByPath)
        {
            var handler = new StubHandler(statusByPath);
            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://cursor.com")
            };
            var factory = new StubHttpClientFactory(httpClient);
            var messenger = new StrongReferenceMessenger();
            var logger = NullLogger<EndpointProbeService>.Instance;
            var service = new EndpointProbeService(factory, messenger, logger);
            return (service, messenger, new object());
        }

        /// <summary>
        /// 按请求路径返回预设状态码的 HttpMessageHandler 桩。
        /// </summary>
        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<string, HttpStatusCode> _statusByPath;

            public StubHandler(Func<string, HttpStatusCode> statusByPath)
            {
                _statusByPath = statusByPath;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri!.AbsolutePath;
                var code = _statusByPath(path);
                return Task.FromResult(new HttpResponseMessage(code)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }
        }

        /// <summary>
        /// 直接返回预设 HttpClient 的 IHttpClientFactory 桩，避免依赖 DI。
        /// </summary>
        private sealed class StubHttpClientFactory : IHttpClientFactory
        {
            private readonly HttpClient _httpClient;

            public StubHttpClientFactory(HttpClient httpClient)
            {
                _httpClient = httpClient;
            }

            public HttpClient CreateClient(string name) => _httpClient;
        }
    }
}
