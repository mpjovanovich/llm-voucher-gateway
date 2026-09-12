using LlmVoucherGateway.Api.Inference;
using Microsoft.AspNetCore.Http.Features;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Relays OpenAI-compatible chat completion requests to the upstream inference server.
/// </summary>
internal static class ChatCompletionsEndpoint
{
    public static void MapChatCompletions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/chat/completions", HandleAsync);
    }

    private static async Task HandleAsync(HttpContext httpContext, IInferenceClient inferenceClient)
    {
        // Tie the upstream request to the caller's connection: a client that walks away
        // tears down the generation instead of leaving it running.
        CancellationToken cancellationToken = httpContext.RequestAborted;

        await using InferenceResponse upstreamResponse = await inferenceClient.CreateChatCompletionAsync(
            httpContext.Request.Body,
            cancellationToken
        );

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
