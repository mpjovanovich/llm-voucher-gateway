using System.Net;
using LlmVoucherGateway.Api.Middleware;
using Serilog.Events;

namespace LlmVoucherGateway.Api.IntegrationTests.Observability;

/// <summary>
/// One line per request, and one id across everything a request logs. What these cannot
/// prove is the client address behind a proxy, which needs forwarded headers and Caddy.
/// </summary>
public class RequestLoggingTests
{
    private const string ModelsPath = "/v1/models";

    [Fact]
    public async Task Request_LogsOneLineWithMethodPathAndStatus()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("HTTP GET /v1/models responded 200 in", factory.LogOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_ThatFailsUnhandled_LogsTheStatusTheCallerSaw()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();
        factory.InferenceClient.Fault = new HttpRequestException("Connection refused");

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        // Not 500: the line is written after the exception handler has chosen a status.
        Assert.Contains("HTTP GET /v1/models responded 502 in", factory.LogOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_DoesNotLogTheQueryString()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync($"{ModelsPath}?api_key=pasted-into-the-url");

        Assert.Contains("HTTP GET /v1/models responded 401", factory.LogOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("pasted-into-the-url", factory.LogOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryLineFromOneRequest_SharesTheCorrelationIdTheCallerWasGiven()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        // Rejected, so the request writes two lines: the rejection and the request line.
        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        string correlationId = Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));

        List<LogEvent> requestLines = [.. factory.LogEvents.Where(logEvent => CorrelationIdOf(logEvent) is not null)];

        Assert.Equal(2, requestLines.Count);
        Assert.All(requestLines, logEvent => Assert.Equal(correlationId, CorrelationIdOf(logEvent)));
    }

    [Fact]
    public async Task TwoRequests_GetDifferentCorrelationIds()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage first = await client.GetAsync(ModelsPath);
        using HttpResponseMessage second = await client.GetAsync(ModelsPath);

        Assert.NotEqual(
            Assert.Single(first.Headers.GetValues(CorrelationIdMiddleware.HeaderName)),
            Assert.Single(second.Headers.GetValues(CorrelationIdMiddleware.HeaderName))
        );
    }

    private static string? CorrelationIdOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue(CorrelationIdMiddleware.PropertyName, out LogEventPropertyValue? value)
        && value is ScalarValue { Value: string correlationId }
            ? correlationId
            : null;
}
