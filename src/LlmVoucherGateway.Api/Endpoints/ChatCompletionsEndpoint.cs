using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Relays OpenAI-compatible chat completion requests to the upstream inference server.
/// </summary>
internal static class ChatCompletionsEndpoint
{
    public static void MapChatCompletions(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost("/v1/chat/completions", HandleAsync)
            .AddEndpointFilter<VoucherRequiredEndpointFilter>();
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

        await upstreamResponse.RelayAsync(httpContext, cancellationToken);
    }
}
