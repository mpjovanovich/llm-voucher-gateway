using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Looks up one voucher by its key prefix, which is the handle every request line logs
/// (<c>as &lt;prefix&gt;</c>). This is the step from a log line to the voucher behind it
/// and what it has consumed.
/// </summary>
internal static class VoucherLookupEndpoint
{
    /// <summary>
    /// Mapped into the <c>/admin</c> group from <see cref="AdminEndpoints"/>, which
    /// supplies both the path prefix and the administrator requirement.
    /// </summary>
    public static void MapVoucherLookup(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/vouchers/{prefix}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string prefix,
        VoucherDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        // The prefix is eight hex characters of a random key and nothing enforces its
        // uniqueness, so a collision is possible in principle. Loading every match
        // rather than the first lets that case be reported instead of answered with
        // whichever row the database returned.
        List<Voucher> matches = await dbContext.Vouchers
            .Where(voucher => voucher.Key.Prefix == prefix)
            .ToListAsync(cancellationToken);

        return matches.Count switch
        {
            0 => Results.NotFound(),
            1 => Results.Ok(VoucherSummaryResponse.From(matches[0])),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ambiguous voucher prefix.",
                detail: $"{matches.Count} vouchers share the prefix. List their section to tell them apart by id."
            ),
        };
    }
}
