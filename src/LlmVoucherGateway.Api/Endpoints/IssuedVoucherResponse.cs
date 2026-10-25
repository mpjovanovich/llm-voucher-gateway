using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// One minted voucher as the caller sees it. <see cref="Key"/> is the bearer secret and
/// this response is the only place it ever exists — the aggregate keeps a hash and the
/// prefix, so a key that is lost here cannot be recovered.
/// </summary>
internal sealed record IssuedVoucherResponse(
    Guid Id,
    string Key,
    string Prefix,
    string? Section,
    int Allowance,
    int Consumed,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt
)
{
    public static IssuedVoucherResponse From(IssuedVoucher issuedVoucher) =>
        new(
            issuedVoucher.Voucher.Id,
            issuedVoucher.PlaintextKey,
            issuedVoucher.Voucher.Key.Prefix,
            issuedVoucher.Voucher.Section,
            issuedVoucher.Voucher.Allowance,
            issuedVoucher.Voucher.Consumed,
            issuedVoucher.Voucher.Status.ToString(),
            issuedVoucher.Voucher.Lifetime.CreatedAt,
            issuedVoucher.Voucher.Lifetime.ExpiresAt
        );
}
