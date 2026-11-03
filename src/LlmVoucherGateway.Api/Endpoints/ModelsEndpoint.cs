using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Relays the upstream model list. A client that lets the user pick a model asks for this
/// before it will send a chat request at all, so without it the gateway is unusable from
/// anything but a client with the model name hardcoded.
/// </summary>
internal static class ModelsEndpoint
{
    public static void MapModels(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet("/v1/models", HandleAsync)
            // Same requirement as chat completions, for two reasons: the served model list
            // is not public, and a client that lists models first turns this into the
            // credential check the user sees — a bad voucher is reported here, before a
            // prompt has been typed, rather than swallowed by a disabled input.

            // For example, the "models" dropdown list in the web UI will hit this endpoint before
            // anything else.
            .AddEndpointFilter<VoucherRequiredEndpointFilter>();
    }

    private static async Task HandleAsync(HttpContext httpContext, IInferenceClient inferenceClient)
    {
        CancellationToken cancellationToken = httpContext.RequestAborted;

        await using InferenceResponse upstreamResponse =
            await inferenceClient.ListModelsAsync(cancellationToken);

        await upstreamResponse.RelayAsync(httpContext, cancellationToken);
    }
}
