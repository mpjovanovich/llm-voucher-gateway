using Microsoft.AspNetCore.Http.Features;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Copies an upstream response out to the caller. Shared by every pass-through endpoint
/// </summary>
internal static class InferenceResponseRelay
{
    public static async Task RelayAsync(
        this InferenceResponse upstreamResponse,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        // Only the status code and Content-Type are forwarded. Copying the rest would drag
        // along Content-Length and Transfer-Encoding, which Kestrel sets for itself.
        httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
        httpContext.Response.ContentType = upstreamResponse.ContentType;

        // Belt-and-braces against response buffering introduced by middleware added later.
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // CopyToAsync flushes as it goes against an unbuffered response body, so streamed
        // chunks reach the caller as they arrive rather than at completion.
        await upstreamResponse.Body.CopyToAsync(httpContext.Response.Body, cancellationToken);
    }
}
