using System.Net;
using System.Text;
using System.Text.Json;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// Replays the Milestone 1 captures in place of a live vLLM server. A class rather than a
/// stub server: the point is that the suite needs no GPU, not that it needs a second
/// process.
/// </summary>
internal sealed class FakeInferenceClient : IInferenceClient
{
    /// <summary>
    /// Bodies of the requests it was asked to forward, so a test can assert that a
    /// rejected request never reached the inference server at all.
    /// </summary>
    public List<string> ForwardedRequests { get; } = [];

    public async Task<InferenceResponse> CreateChatCompletionAsync(
        Stream requestBody,
        CancellationToken cancellationToken
    )
    {
        using StreamReader reader = new(requestBody);
        string request = await reader.ReadToEndAsync(cancellationToken);

        ForwardedRequests.Add(request);

        bool streaming = IsStreamingRequest(request);

        string body = streaming
            ? CapturedResponses.StreamingBody()
            : CapturedResponses.NonStreamingBody();

        // InferenceResponse owns the message and disposes it, which CA2000 cannot see
        // across the constructor.
#pragma warning disable CA2000
        return new InferenceResponse(new HttpResponseMessage())
#pragma warning restore CA2000
        {
            StatusCode = HttpStatusCode.OK,
            ContentType = streaming ? "text/event-stream" : "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(body)),
        };
    }

    /// <summary>
    /// The real client forwards the body without inspection; this one has to read it,
    /// because which capture to replay depends on what was asked for.
    /// </summary>
    private static bool IsStreamingRequest(string request)
    {
        using var document = JsonDocument.Parse(request);

        return document.RootElement.TryGetProperty("stream", out JsonElement stream)
            && stream.ValueKind == JsonValueKind.True;
    }
}
