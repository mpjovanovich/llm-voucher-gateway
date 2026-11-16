using LlmVoucherGateway.Api.Persistence;
using LlmVoucherGateway.Domain.Vouchers;
using Microsoft.EntityFrameworkCore;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Withdraws the voucher behind a request-line prefix. A prefix that matches more than
/// one voucher matches none for this purpose: the call revokes nothing.
/// </summary>
internal static class VoucherRevocationEndpoint
{
    /// <summary>
    /// Mapped into the <c>/admin</c> group from <see cref="AdminEndpoints"/>, which
    /// supplies both the path prefix and the administrator requirement.
    /// </summary>
    public static void MapVoucherRevocation(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/vouchers/{prefix}/revoke", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string prefix,
        RevokeVoucherRequest? request,
        VoucherDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        // The prefix is eight hex characters of a random key and nothing enforces its
        // uniqueness. Every match is loaded so a collision is refused rather than
        // resolved by whichever row the database returned first. That refusal comes
        // before the reason is considered, so a bad body cannot be mistaken for a
        // revocation that merely needs a reason filled in.
        List<Voucher> matches = await dbContext.Vouchers
            .Where(voucher => voucher.Key.Prefix == prefix)
            .ToListAsync(cancellationToken);

        return matches.Count switch
        {
            0 => Results.NotFound(),
            1 => await RevokeAsync(matches[0], request, dbContext, cancellationToken),
            _ => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ambiguous voucher prefix.",
                detail: $"{matches.Count} vouchers share the prefix. Nothing was revoked."
            ),
        };
    }

    private static async Task<IResult> RevokeAsync(
        Voucher voucher,
        RevokeVoucherRequest? request,
        VoucherDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        // A missing reason throws before any state changes, so the handler's 400 leaves
        // the row as it was. A voucher that is already revoked keeps its first reason.
        voucher.Revoke(request?.Reason);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(VoucherSummaryResponse.From(voucher));
    }
}
