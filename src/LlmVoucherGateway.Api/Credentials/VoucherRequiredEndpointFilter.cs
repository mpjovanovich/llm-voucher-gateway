using LlmVoucherGateway.Api.Errors;
using LlmVoucherGateway.Domain.Abstractions;
using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Requires a spendable voucher. The middleware resolved who the caller is; this decides
/// whether they may spend, and says why not in a format the client already understands.
/// </summary>
internal sealed class VoucherRequiredEndpointFilter(
    IDateTimeProvider dateTimeProvider,
    ILogger<VoucherRequiredEndpointFilter> logger
) : IEndpointFilter
{
    private const string VoucherRequiredMessage =
        "A voucher is required. Set it as a bearer token in the Authorization header.";

    private const string RevokedMessage = "This voucher has been revoked.";

    private const string ExpiredMessage = "This voucher has expired.";

    private const string ExhaustedMessage = "This voucher's token allowance is used up.";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        CallerCredential? credential = context.HttpContext.Features.Get<CallerCredential>();

        // An admin key is not a voucher. Letting it through would be an unmetered path
        // through the one endpoint that costs GPU time, which is the thing this gateway
        // exists to meter; an operator who wants to try the endpoint mints a voucher.
        if (credential?.Voucher is not Voucher voucher)
        {
            return Reject(
                context,
                OpenAiErrorResults.InvalidCredential(VoucherRequiredMessage),
                "no voucher credential"
            );
        }

        VoucherSpendability spendability = voucher.CheckSpendability(dateTimeProvider);

        if (spendability is VoucherSpendability.Spendable)
        {
            return await next(context);
        }

        return spendability switch
        {
            VoucherSpendability.Revoked => Reject(
                context,
                OpenAiErrorResults.InvalidCredential(RevokedMessage),
                "voucher revoked"
            ),
            VoucherSpendability.Expired => Reject(
                context,
                OpenAiErrorResults.InvalidCredential(ExpiredMessage),
                "voucher expired"
            ),
            VoucherSpendability.Exhausted => Reject(
                context,
                OpenAiErrorResults.AllowanceExhausted(ExhaustedMessage),
                "voucher allowance exhausted"
            ),

            // A reason added to the enum later is refused rather than admitted, so
            // forgetting to map it cannot open the endpoint.
            _ => Reject(
                context,
                OpenAiErrorResults.InvalidCredential(VoucherRequiredMessage),
                "voucher unusable"
            ),
        };
    }

    // Warning, as for admin rejections: a refused request is the signal a post-mortem
    // looks for. The reason is what separates "wrong key" from "used it up" afterwards.
    // No voucher id or prefix, and never the presented token — the time and the address
    // are the handle for querying the database.
    private IResult Reject(EndpointFilterInvocationContext context, IResult rejection, string reason)
    {
        logger.LogWarning(
            "Rejected {Method} {Path} from {SourceIpAddress}: {Reason}.",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path,
            context.HttpContext.Connection.RemoteIpAddress,
            reason
        );

        return rejection;
    }
}
