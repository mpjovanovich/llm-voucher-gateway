using System.Net;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// An upstream response reduced to what the endpoint needs in order to relay it, so that
/// consumers of <see cref="IInferenceClient"/> never become <see cref="HttpClient"/>
/// participants.
/// </summary>
/// <remarks>
/// Owns the underlying <see cref="HttpResponseMessage"/>. Disposing this is what releases
/// the upstream connection, so callers must dispose it once <see cref="Body"/> has been
/// copied out — and not before.
/// </remarks>
internal sealed class InferenceResponse : IAsyncDisposable
{
    private readonly HttpResponseMessage upstreamResponse;

    public InferenceResponse(HttpResponseMessage upstreamResponse)
    {
        this.upstreamResponse = upstreamResponse;
    }

    public required HttpStatusCode StatusCode { get; init; }

    public required string? ContentType { get; init; }

    /// <summary>
    /// The unread upstream body. Bytes are pulled off the wire as they are read, which is
    /// what keeps a streaming response unbuffered end to end.
    /// </summary>
    public required Stream Body { get; init; }

    public async ValueTask DisposeAsync()
    {
        await Body.DisposeAsync();
        upstreamResponse.Dispose();
    }
}
