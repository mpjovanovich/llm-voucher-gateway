using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Abstractions;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.Extensions.Options;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Mints a batch of vouchers and returns it. The response body is the only record of
/// the plaintext keys, so whoever called this distributes them; the API writes no file.
/// </summary>
internal static class VoucherIssuanceEndpoint
{
    /// <summary>
    /// Mapped into the <c>/admin</c> group from <see cref="AdminEndpoints"/>, which
    /// supplies both the path prefix and the administrator requirement.
    /// </summary>
    public static void MapVoucherIssuance(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/vouchers", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        IssueVouchersRequest request,
        IDateTimeProvider dateTimeProvider,
        IOptions<VoucherIssuanceOptions> issuanceOptions,
        VoucherDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        // Count is the one rule the domain has no opinion on — it mints one voucher at a
        // time. Allowance and expiry are the aggregate's rules, so they are left to it
        // and reach the caller as a 400 through the exception handler.
        int maxPerRequest = issuanceOptions.Value.MaxPerRequest;

        if (request.Count <= 0 || request.Count > maxPerRequest)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["count"] = [$"Count must be between 1 and {maxPerRequest}."],
                }
            );
        }

        List<IssuedVoucherResponse> issuedVouchers = new(request.Count);

        for (int index = 0; index < request.Count; index++)
        {
            IssuedVoucher issuedVoucher = Voucher.Issue(
                request.Section,
                request.Allowance,
                request.ExpiresAt,
                dateTimeProvider
            );

            dbContext.Vouchers.Add(issuedVoucher.Voucher);
            issuedVouchers.Add(IssuedVoucherResponse.From(issuedVoucher));
        }

        // One save for the whole batch, so either every voucher in the response exists
        // or none does. A partial write would hand out keys the gateway cannot honour,
        // and the response is the only copy of them.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(issuedVouchers);
    }
}
