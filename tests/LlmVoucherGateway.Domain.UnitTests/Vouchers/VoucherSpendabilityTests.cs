using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Domain.UnitTests.Vouchers;

public class VoucherSpendabilityTests
{
    private static readonly DateTime IssuedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset ExpiresAt = new DateTimeOffset(IssuedAt).AddDays(30);

    private static Voucher IssuedVoucher() =>
        Voucher.Issue(
            "CSCI-101",
            allowance: 100_000,
            ExpiresAt,
            new FakeDateTimeProvider(IssuedAt)
        ).Voucher;

    [Fact]
    public void CheckSpendability_BeforeExpiry_IsSpendable()
    {
        Voucher voucher = IssuedVoucher();

        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(ExpiresAt.UtcDateTime.AddTicks(-1))
        );

        Assert.Equal(VoucherSpendability.Spendable, spendability);
    }

    [Fact]
    public void CheckSpendability_AtTheInstantOfExpiry_IsExpired()
    {
        Voucher voucher = IssuedVoucher();

        // The boundary itself, which is where an off-by-one would hide: a voucher is
        // valid up to its expiry and not including it.
        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(ExpiresAt.UtcDateTime)
        );

        Assert.Equal(VoucherSpendability.Expired, spendability);
    }

    [Fact]
    public void CheckSpendability_AfterExpiry_IsExpired()
    {
        Voucher voucher = IssuedVoucher();

        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(ExpiresAt.UtcDateTime.AddDays(1))
        );

        Assert.Equal(VoucherSpendability.Expired, spendability);
    }
}
