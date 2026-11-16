using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Lists the vouchers issued to a section with their status and consumption, which is
/// how an operator finds the voucher behind a request line or sees who has used what.
/// </summary>
internal static class SectionVouchersEndpoint
{
    /// <summary>
    /// Mapped into the <c>/admin</c> group from <see cref="AdminEndpoints"/>, which
    /// supplies both the path prefix and the administrator requirement.
    /// </summary>
    public static void MapSectionVouchers(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/sections/{section}/vouchers", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string section,
        VoucherDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        // A section is a label stamped on a batch, not a record of its own, so an
        // unknown one is simply a section with no vouchers rather than a 404.
        List<Voucher> vouchers = await dbContext.Vouchers
            .Where(voucher => voucher.Section == section)
            .ToListAsync(cancellationToken);

        // Ordered in memory: SQLite cannot ORDER BY a DateTimeOffset column, and a
        // section holds a classroom's worth of rows, not enough to matter.
        var summaries = vouchers
            .OrderBy(voucher => voucher.Lifetime.CreatedAt)
            .ThenBy(voucher => voucher.Key.Prefix, StringComparer.Ordinal)
            .Select(VoucherSummaryResponse.From)
            .ToList();

        return Results.Ok(summaries);
    }
}
