using System.Net;
using System.Text;
using CursorUsageNotify.Core.Configuration;
using CursorUsageNotify.Services.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CursorUsageNotify.Tests.Services;

/// <summary>
/// CursorApiClient 单元测试：用 StubHttpMessageHandler 模拟 HTTP 响应。
/// </summary>
public class CursorApiClientTests
{
    private const string SampleEventsResponse = """
        {
            "totalUsageEventsCount": 1,
            "usageEventsDisplay": [
                {
                    "timestamp": "2024-01-01T00:00:00.000Z",
                    "userEmail": "test@example.com",
                    "model": "claude-4.5-sonnet",
                    "kind": "Usage-based",
                    "tokenUsage": {"inputTokens": 100, "outputTokens": 50, "totalCents": 12.5}
                }
            ]
        }
        """;

    private const string SamplePeriodResponse = """
        {
            "periodStart": 1704067200000,
            "periodEnd": 1706745599000,
            "planName": "Pro",
            "includedRequests": 500,
            "usedRequests": 100,
            "usedTokens": 50000,
            "totalSpendCents": 200,
            "remainingRequests": 400
        }
        """;

    [Fact]
    public async Task TestConnectionAsync_ValidToken_ReturnsOkWithCount()
    {
        var handler = new StubHttpMessageHandler(SampleEventsResponse, HttpStatusCode.OK);
        var client = CreateClient(handler);

        var result = await client.TestConnectionAsync("valid-token");

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value);
    }

    [Fact]
    public async Task TestConnectionAsync_Unauthorized_ReturnsFailWithAuthMessage()
    {
        var handler = new StubHttpMessageHandler("{}", HttpStatusCode.Unauthorized);
        var client = CreateClient(handler);

        var result = await client.TestConnectionAsync("bad-token");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GetFilteredUsageEventsAsync_ValidResponse_ReturnsEvents()
    {
        var handler = new StubHttpMessageHandler(SampleEventsResponse, HttpStatusCode.OK);
        var client = CreateClient(handler);

        var response = await client.GetFilteredUsageEventsAsync("token", page: 1, pageSize: 10);

        Assert.Equal(1, response.TotalUsageEventsCount);
        Assert.Single(response.UsageEventsDisplay);
    }

    [Fact]
    public async Task GetCurrentPeriodUsageAsync_ValidResponse_ReturnsDto()
    {
        var handler = new StubHttpMessageHandler(SamplePeriodResponse, HttpStatusCode.OK);
        var client = CreateClient(handler);

        var dto = await client.GetCurrentPeriodUsageAsync("token");

        Assert.Equal("Pro", dto.PlanName);
        Assert.Equal(500, dto.IncludedRequests);
        Assert.Equal(200, dto.TotalSpendCents);
    }

    private static CursorApiClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cursor.com")
        };
        var settings = new AppSettings();
        return new CursorApiClient(httpClient, settings, NullLogger<CursorApiClient>.Instance);
    }
}

/// <summary>
/// 简单的 HttpMessageHandler 桩，返回预设的响应。
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    public StubHttpMessageHandler(string body, HttpStatusCode statusCode)
    {
        _response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_response);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _response.Dispose();
        }
        base.Dispose(disposing);
    }
}
