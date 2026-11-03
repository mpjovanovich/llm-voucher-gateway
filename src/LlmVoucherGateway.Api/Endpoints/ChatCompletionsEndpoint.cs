using LlmVoucherGateway.Api.Credentials;
using LlmVoucherGateway.Api.Inference;
using LlmVoucherGateway.Api.Persistence;
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
        VoucherDbContext dbContext
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

        await upstreamResponse.RelayAsync(httpContext, usageObserver, cancellationToken);

        await RecordConsumptionAsync(httpContext, dbContext, usageObserver, cancellationToken);
    }

    // Recorded after the response, against the voucher the filter already checked: the
    // budget is soft, so a request admitted under it is allowed to finish over it.
    private static async Task RecordConsumptionAsync(
        HttpContext httpContext,
        VoucherDbContext dbContext,
        IUsageObserver usageObserver,
        CancellationToken cancellationToken
    )
    {
        if (httpContext.Features.Get<CallerCredential>()?.Voucher is not Voucher voucher)
        {
            return;
        }

        if (usageObserver.ReadTotalTokens() is not int totalTokens)
        {
            return;
        }

        voucher.RecordConsumption(totalTokens);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
