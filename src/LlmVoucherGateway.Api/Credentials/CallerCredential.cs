namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Who a request's bearer token turned out to belong to. An unrecognized token
/// produces no credential at all, so every instance represents a caller the
/// gateway accepted.
/// </summary>
internal sealed record CallerCredential
{
    private CallerCredential(Guid? voucherId, bool isAdmin)
    {
        VoucherId = voucherId;
        IsAdmin = isAdmin;
    }

    /// <summary>
    /// The voucher the token resolved to, or <c>null</c> for an administrator.
    /// Administrators are configured keys rather than issued vouchers, so they
    /// have no allowance to meter against.
    /// </summary>
    public Guid? VoucherId { get; }

    public bool IsAdmin { get; }

    public static CallerCredential ForAdmin() => new(voucherId: null, isAdmin: true);

    public static CallerCredential ForVoucher(Guid voucherId) => new(voucherId, isAdmin: false);
}
