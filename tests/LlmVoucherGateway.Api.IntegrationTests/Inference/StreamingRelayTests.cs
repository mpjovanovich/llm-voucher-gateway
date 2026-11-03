using System.Net.Http.Json;
using System.Text;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// Proves the relay forwards a streamed response as it arrives rather than accumulating it.
/// </summary>
/// <remarks>
/// A relay that buffers is not a broken relay from the client's point of view — it is a
/// slow one, and against a long generation it looks like a hang rather than a failure.
/// That makes it the kind of regression that is cheap to catch here and expensive to
/// diagnose anywhere else, which is why it gets a test of its own despite needing a
/// purpose-built upstream body to see it.
/// </remarks>
public class StreamingRelayTests
{
    private const string ChatCompletionsPath = "/v1/chat/completions";

    private const string FirstChunk = "data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n";

    private const string SecondChunk = "data: [DONE]\n\n";

    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private static readonly object StreamingRequest = new
    {
        model = "Qwen/Qwen2.5-0.5B-Instruct",
        messages = new[] { new { role = "user", content = "Say hello in one sentence." } },
        stream = true,
    };

    [Fact]
    public async Task ChatCompletions_WhenStreamed_ForwardsEachChunkBeforeTheNextIsAvailable()
    {
        await using GatewayApplicationFactory factory = new();
        using HttpClient client = await factory.CreateVoucherClientAsync();

        ReleasedChunkStream upstreamBody = new(FirstChunk, SecondChunk);
        factory.InferenceClient.StreamingBody = upstreamBody;

        using HttpRequestMessage request = new(HttpMethod.Post, ChatCompletionsPath)
        {
            Content = JsonContent.Create(StreamingRequest),
        };

        // A relay that accumulates the body never writes anything here, so every step
        // below would otherwise block until HttpClient's own 100-second timeout and then
        // report an abort. The token keeps the failure fast and attributable instead.
        using CancellationTokenSource timeout = new(ReadTimeout);

        // ResponseHeadersRead, so the body can be read while it is still being written.
        // The default would wait for the whole response and defeat the test.
        using HttpResponseMessage response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token
        );

        await using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);

        // The assertion that matters: the second chunk does not exist upstream yet, so
        // reaching this at all means the first was forwarded on its own.
        Assert.Equal(FirstChunk, await ReadChunkAsync(body, timeout.Token));

        upstreamBody.Release();

        // And the rest still arrives afterwards, so forwarding early did not truncate it.
        Assert.Equal(SecondChunk, await ReadChunkAsync(body, timeout.Token));
    }

    /// <summary>
    /// Reads one chunk, turning "nothing arrived" into a failure that says why. Buffering
    /// presents as silence rather than an error, so without this the diagnosis is a
    /// cancelled read with nothing to point at.
    /// </summary>
    private static async Task<string> ReadChunkAsync(Stream body, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[256];

        int count;

        try
        {
            count = await body.ReadAsync(buffer, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw FailedToArrive();
        }
        catch (IOException)
        {
            // The host aborts the request once the read is cancelled, which can surface
            // here instead of the cancellation itself.
            throw FailedToArrive();
        }

        return Encoding.UTF8.GetString(buffer, 0, count);
    }

    private static Xunit.Sdk.XunitException FailedToArrive() =>
        new(
            $"No chunk reached the caller within {ReadTimeout.TotalSeconds:0} seconds. The "
                + "relay is accumulating the upstream body instead of forwarding it as it arrives."
        );
}
