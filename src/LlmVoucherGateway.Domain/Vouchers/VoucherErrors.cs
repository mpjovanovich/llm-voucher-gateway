namespace LlmVoucherGateway.Domain.Vouchers;

internal static class VoucherErrors
{
    public const string AllowanceMustBePositive = "Voucher allowance must be positive";
    public const string ExpiryMustBeInFuture = "Voucher expiry must be in future";
    public const string KeyHashRequired = "Voucher key missing hash";
    public const string KeyPrefixRequired = "Voucher key missing prefix";
    public const string PlaintextValueRequired = "Voucher plaintext value is required";
}
