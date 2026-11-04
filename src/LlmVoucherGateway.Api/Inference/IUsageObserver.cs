using System.Net.Http.Headers;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Watches an upstream body go past on its way to the caller and reports the token count
/// it carried. Never alters or holds back the bytes it is shown.
/// </summary>
internal interface IUsageObserver
{
    void Observe(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// The count from what has been observed so far, or <c>null</c> when there was none.
    /// </summary>
    int? ReadTotalTokens();


    // This is in place because the OpenAI client may request a streaming
    // response, in which case vLLM returns an event stream. Otherwise it
    // returns json.

    /// <summary>
    /// Picks an observer from the upstream response's <c>Content-Type</c>. A caller
    /// that sets <c>"stream": true</c> gets <c>text/event-stream</c> back from vLLM,
    /// and the count is taken from the last chunk. Any other content type is one
    /// JSON document.
    /// </summary>
    static IUsageObserver ForContentType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaType)
        && string.Equals(mediaType.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            ? new StreamedUsageObserver()
            : new BufferedUsageObserver();
}
