using System.Net;
using System.Net.Http.Headers;
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

    // VoucherKeyGenerator takes the prefix from the front of the key.
    private const int PrefixLength = 12;

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

    [Fact]
    public async Task Request_WithAVoucher_LogsItsPrefixAndNotItsKey()
    {
        await using GatewayApplicationFactory factory = new();
        string voucherKey = await factory.IssueVoucherKeyAsync();
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", voucherKey);

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The prefix is the handle for grouping one caller's requests; the key is the
        // secret and must never appear, prefix or not.
        Assert.Contains($"as {voucherKey[..PrefixLength]}", factory.LogOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(voucherKey, factory.LogOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_WithNoCredential_NamesNoVoucher()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("responded 401 in", factory.LogOutput, StringComparison.Ordinal);
        Assert.Contains("as unknown", factory.LogOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_WithAnAdminKey_LogsTheCallerAsAdminAndNotTheKey()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = factory.CreateAdminClient();

        using HttpResponseMessage response = await client.GetAsync(ModelsPath);

        // An admin key does not pass /v1, but it still resolves, so the line names it.
        Assert.Contains("as admin", factory.LogOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(
            GatewayApplicationFactory.ConfiguredAdminKey,
            factory.LogOutput,
            StringComparison.Ordinal
        );
    }

    private static string? CorrelationIdOf(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue(CorrelationIdMiddleware.PropertyName, out LogEventPropertyValue? value)
        && value is ScalarValue { Value: string correlationId }
            ? correlationId
            : null;
}
