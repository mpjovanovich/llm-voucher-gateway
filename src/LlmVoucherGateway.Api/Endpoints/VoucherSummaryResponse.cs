using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// One voucher as an operator sees it after issuance. Deliberately has no key field:
/// the plaintext left in the issuance response and the database holds only a hash, so
/// there is nothing to show. The prefix is how a row is matched to a request line.
/// <c>RevocationReason</c> is null until the voucher is revoked.
/// </summary>
internal sealed record VoucherSummaryResponse(
    Guid Id,
    string Prefix,
    string? Section,
    int Allowance,
    int Consumed,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? LastRequestedAt,
    string? RevocationReason
)
{
    public static VoucherSummaryResponse From(Voucher voucher) =>
        new(
            voucher.Id,
            voucher.Key.Prefix,
            voucher.Section,
            voucher.Allowance,
            voucher.Consumed,
            voucher.Status.ToString(),
            voucher.Lifetime.CreatedAt,
            voucher.Lifetime.ExpiresAt,
            voucher.LastRequestedAt,
            voucher.RevocationReason
        );
}
