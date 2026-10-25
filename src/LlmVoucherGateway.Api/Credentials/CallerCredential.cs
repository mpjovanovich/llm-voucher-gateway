using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Who a request's bearer token turned out to belong to. An unrecognized token
/// produces no credential at all, so every instance represents a caller the
/// gateway accepted.
/// </summary>
internal sealed record CallerCredential
{
    private CallerCredential(Voucher? voucher, bool isAdmin)
    {
        Voucher = voucher;
        IsAdmin = isAdmin;
    }

    /// <summary>
    /// The voucher the token resolved to, or <c>null</c> for an administrator.
    /// Administrators are configured keys rather than issued vouchers, so they
    /// have no allowance to meter against.
    /// </summary>
    /// <remarks>
    /// The aggregate itself rather than its id: it was loaded to resolve the token, it is
    /// still tracked by the request's <c>DbContext</c>, and whoever decides whether the
    /// request may proceed needs its state. Carrying the id instead would mean reading
    /// the same row twice per request.
    /// </remarks>
    public Voucher? Voucher { get; }

    public Guid? VoucherId => Voucher?.Id;

    public bool IsAdmin { get; }

    public static CallerCredential ForAdmin() => new(voucher: null, isAdmin: true);

    public static CallerCredential ForVoucher(Voucher voucher) => new(voucher, isAdmin: false);
}
