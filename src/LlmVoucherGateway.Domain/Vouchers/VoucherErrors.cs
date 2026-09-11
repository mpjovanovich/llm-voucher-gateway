namespace LlmVoucherGateway.Domain.Vouchers;

public static class VoucherErrors
{
    public const string KeyHashRequired = "voucher.key_hash_required";
    public const string KeyPrefixRequired = "voucher.key_prefix_required";
    public const string SectionRequired = "voucher.section_required";
    public const string AllowanceMustBePositive = "voucher.allowance_must_be_positive";
    public const string ExpiryMustBeInFuture = "voucher.expiry_must_be_in_future";
}
