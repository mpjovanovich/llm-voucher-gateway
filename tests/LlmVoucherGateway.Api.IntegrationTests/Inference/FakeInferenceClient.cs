using System.Net;
using System.Text;
using System.Text.Json;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

internal sealed class FakeInferenceClient : IInferenceClient
{
    public const string ModelListBody =
        """
        {"object":"list","data":[{"id":"Qwen/Qwen2.5-0.5B-Instruct","object":"model","created":1757692800,"owned_by":"vllm"}]}
        """;

    /// <summary>
    /// Bodies of the requests it was asked to forward, so a test can assert that a
    /// rejected request never reached the inference server at all.
    /// </summary>
    public List<string> ForwardedRequests { get; } = [];

    /// <summary>
    /// How many times the model list was asked for upstream, so a rejection test can ask
    /// the same question for this path that <see cref="ForwardedRequests"/> answers for
    /// chat completions.
    /// </summary>
    public int ListModelsCalls { get; private set; }

    /// <summary>
    /// Replaces the streamed response body. Set it when a test needs to control when the
    /// upstream bytes arrive rather than have them all available at once.
    /// </summary>
    public Stream? StreamingBody { get; set; }

    /// <summary>
    /// Replaces the captured response entirely, for an upstream that answers differently
    /// from the dev server — an error, or a stream without usage.
    /// </summary>
    public ScriptedResponse? ChatCompletionResponse { get; set; }

    public async Task<InferenceResponse> CreateChatCompletionAsync(
        Stream requestBody,
        CancellationToken cancellationToken
    )
    {
        using StreamReader reader = new(requestBody);
        string request = await reader.ReadToEndAsync(cancellationToken);

        ForwardedRequests.Add(request);

        if (ChatCompletionResponse is ScriptedResponse scripted)
        {
            // InferenceResponse owns the message and disposes it, which CA2000 cannot see
            // across the constructor.
#pragma warning disable CA2000
            return new InferenceResponse(new HttpResponseMessage())
#pragma warning restore CA2000
            {
                StatusCode = scripted.StatusCode,
                ContentType = scripted.ContentType,
                Body = new MemoryStream(Encoding.UTF8.GetBytes(scripted.Body)),
            };
        }

        bool streaming = IsStreamingRequest(request);

        string capture = streaming
            ? CapturedResponses.StreamingBodyWithUsage()
            : CapturedResponses.NonStreamingBody();

        Stream body = streaming && StreamingBody is not null
            ? StreamingBody
            : new MemoryStream(Encoding.UTF8.GetBytes(capture));

        // InferenceResponse owns the message and disposes it, which CA2000 cannot see
        // across the constructor.
#pragma warning disable CA2000
        return new InferenceResponse(new HttpResponseMessage())
#pragma warning restore CA2000
        {
            StatusCode = HttpStatusCode.OK,
            ContentType = streaming ? "text/event-stream" : "application/json",
            Body = body,
        };
    }

    public Task<InferenceResponse> ListModelsAsync(CancellationToken cancellationToken)
    {
        ListModelsCalls++;

        // InferenceResponse owns the message and disposes it, which CA2000 cannot see
        // across the constructor.
#pragma warning disable CA2000
        InferenceResponse response = new(new HttpResponseMessage())
#pragma warning restore CA2000
        {
            StatusCode = HttpStatusCode.OK,
            ContentType = "application/json",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(ModelListBody)),
        };

        return Task.FromResult(response);
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

internal sealed record ScriptedResponse(HttpStatusCode StatusCode, string ContentType, string Body);
