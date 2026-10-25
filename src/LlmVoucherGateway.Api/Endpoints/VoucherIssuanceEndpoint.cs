using System.Net;
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
    // ILogger<T> cannot name this class, because C# forbids a static class as a generic
    // type argument, so the category is spelled out instead.
    private const string LogCategory = "LlmVoucherGateway.Api.Endpoints.VoucherIssuance";

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
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken
    )
    {
        // Logged on entry rather than after the save: the signal being recorded is that
        // somebody asked, which matters whether or not the request succeeded, and an
        // attempt that fails validation leaves no row behind to find later. Serilog
        // stamps the time, and that plus the address is enough to go to the database
        // for what was actually minted — so nothing identifying a voucher is logged
        // here, and the `key` field never is, at any level.
        // Behind the reverse proxy added in Milestone 9 this is the proxy unless
        // forwarded headers are configured there.
        IPAddress? sourceIpAddress = httpContext.Connection.RemoteIpAddress;

        loggerFactory.CreateLogger(LogCategory).LogInformation(
            "Voucher issuance requested from {SourceIpAddress} for section {Section}, count {RequestedCount}.",
            sourceIpAddress,
            request.Section,
            request.Count
        );

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

        // Logged after the save, so the line records vouchers that exist rather than an
        // attempt that may have failed. Ids and prefixes are the audit trail for secrets
        // that live in exactly one response body: they identify a voucher without being
        // able to spend it. The `key` field is never logged, and neither is the response.
        return Results.Ok(issuedVouchers);
    }
}
