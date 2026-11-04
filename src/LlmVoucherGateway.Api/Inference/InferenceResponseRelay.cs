using System.Buffers;
using Microsoft.AspNetCore.Http.Features;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Copies an upstream response out to the caller. Shared by every pass-through endpoint
/// </summary>
internal static class InferenceResponseRelay
{
    private const int BufferSize = 81_920;

    public static Task RelayAsync(
        this InferenceResponse upstreamResponse,
        HttpContext httpContext,
        CancellationToken cancellationToken
    ) => upstreamResponse.RelayAsync(httpContext, usageObserver: null, cancellationToken);

    /// <param name="usageObserver">
    /// Shown every chunk after it has been written to the caller. Still holds what it saw
    /// if the relay ends early, so a disconnected request can be metered.
    /// </param>
    public static async Task RelayAsync(
        this InferenceResponse upstreamResponse,
        HttpContext httpContext,
        IUsageObserver? usageObserver,
        CancellationToken cancellationToken
    )
    {
        // Only the status code and Content-Type are forwarded. Copying the rest would drag
        // along Content-Length and Transfer-Encoding, which Kestrel sets for itself.
        httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
        httpContext.Response.ContentType = upstreamResponse.ContentType;

        // Belt-and-braces against response buffering introduced by middleware added later.
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            // Each chunk is written as soon as it is read, which against an unbuffered
            // response body means streamed chunks reach the caller as they arrive rather
            // than at completion.
            int read;

            while ((read = await upstreamResponse.Body.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await httpContext.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                usageObserver?.Observe(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
