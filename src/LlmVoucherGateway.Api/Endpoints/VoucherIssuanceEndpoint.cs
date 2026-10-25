using LlmVoucherGateway.Domain.Abstractions;
using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Mints a batch of vouchers and returns it. The response body is the only record of
/// the plaintext keys, so whoever called this distributes them; the API writes no file.
/// </summary>
internal static class VoucherIssuanceEndpoint
{
    public static void MapVoucherIssuance(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/vouchers", Handle);
    }

    private static IResult Handle(IssueVouchersRequest request, IDateTimeProvider dateTimeProvider)
    {
        // Count is the one rule the domain has no opinion on — it mints one voucher at a
        // time. Allowance and expiry are the aggregate's rules, so they are left to it
        // and reach the caller as a 400 through the exception handler.
        if (request.Count <= 0)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["count"] = ["Count must be a positive integer."],
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

            issuedVouchers.Add(IssuedVoucherResponse.From(issuedVoucher));
        }

        return Results.Ok(issuedVouchers);
    }
}
