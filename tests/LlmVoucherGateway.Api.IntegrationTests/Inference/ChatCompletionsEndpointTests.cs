using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// First tests of the relay itself. They run against the captured Milestone 1 responses,
/// so they prove the gateway's own behaviour without a GPU — what they cannot prove is
/// that streaming stays unbuffered, which only shows up against a real server.
/// </summary>
public class ChatCompletionsEndpointTests
{
    private const string ChatCompletionsPath = "/v1/chat/completions";

    private static readonly object NonStreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
    };

    private static readonly object StreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
        stream = true,
    };

    [Fact]
    public async Task ChatCompletions_RelaysTheUpstreamResponseVerbatim()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ChatCompletionsPath,
            NonStreamingRequest
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        // Byte-for-byte: the relay parses nothing, and Milestone 7 is the first time it
        // will look inside a response at all.
        Assert.Equal(CapturedResponses.NonStreamingBody(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChatCompletions_ForwardsTheRequestBodyUnchanged()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ChatCompletionsPath,
            NonStreamingRequest
        );

        string forwarded = Assert.Single(factory.InferenceClient.ForwardedRequests);

        Assert.Equal(
            JsonSerializer.Serialize(NonStreamingRequest),
            forwarded
        );
    }

    [Fact]
    public async Task ChatCompletions_WithStreamRequested_RelaysSseEndingInDone()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ChatCompletionsPath,
            StreamingRequest
        );

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("data: {", body, StringComparison.Ordinal);
        Assert.Contains("\"finish_reason\":\"stop\"", body, StringComparison.Ordinal);

        // The terminator a harness waits for. Without it a client hangs rather than
        // failing, which is the hardest kind of break to diagnose.
        Assert.EndsWith("data: [DONE]\n\n", body, StringComparison.Ordinal);
    }
}
