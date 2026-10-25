namespace LlmVoucherGateway.Domain.Vouchers;

public enum VoucherSpendability
{
    Spendable,
    Revoked,
    Expired,
    Exhausted,
}
