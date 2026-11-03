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

    /// <summary>
    /// The observer for a response's shape: an event stream holds its count in the last
    /// chunk, anything else is read as a single JSON document.
    /// </summary>
    static IUsageObserver ForContentType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaType)
        && string.Equals(mediaType.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            ? new StreamedUsageObserver()
            : new BufferedUsageObserver();
}
