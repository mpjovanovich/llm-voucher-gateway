using LlmVoucherGateway.Api.Consumption;
using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Domain.Vouchers;

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

    private static async Task HandleAsync(
        HttpContext httpContext,
        IInferenceClient inferenceClient,
        ConsumptionRecorder consumptionRecorder
    )
    {
        // Tie the upstream request to the caller's connection: a client that walks away
        // tears down the generation instead of leaving it running.
        CancellationToken cancellationToken = httpContext.RequestAborted;

        await using InferenceResponse upstreamResponse = await inferenceClient.CreateChatCompletionAsync(
            httpContext.Request.Body,
            cancellationToken
        );

        var usageObserver = IUsageObserver.ForContentType(upstreamResponse.ContentType);

        try
        {
            await upstreamResponse.RelayAsync(httpContext, usageObserver, cancellationToken);
        }
        finally
        {
            // Also on a disconnect: the tokens up to the last chunk that got out were
            // generated all the same. The request's token is already cancelled by then,
            // so the save runs without it.

            // The filter only admits voucher credentials, so this always matches.
            if (httpContext.Features.Get<CallerCredential>()?.Voucher is Voucher voucher)
            {
                await consumptionRecorder.RecordAsync(voucher, usageObserver, CancellationToken.None);
            }
        }
    }
}
