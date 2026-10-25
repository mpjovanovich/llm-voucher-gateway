using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Domain.UnitTests.Vouchers;

public class VoucherSpendabilityTests
{
    private static readonly DateTime IssuedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset ExpiresAt = new DateTimeOffset(IssuedAt).AddDays(30);

    private const int Allowance = 100_000;

    private static Voucher IssuedVoucher() =>
        Voucher.Issue("CSCI-101", Allowance, ExpiresAt, new FakeDateTimeProvider(IssuedAt)).Voucher;

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
    public void CheckSpendability_WhenRevoked_IsRevoked()
    {
        Voucher voucher = IssuedVoucher();
        voucher.Revoke();

        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(IssuedAt)
        );

        Assert.Equal(VoucherSpendability.Revoked, spendability);
    }

    [Fact]
    public void CheckSpendability_WhenRevokedAndExpired_ReportsRevoked()
    {
        Voucher voucher = IssuedVoucher();
        voucher.Revoke();

        // Precedence: an administrator withdrawing a voucher is the more actionable
        // answer for whoever is holding it.
        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(ExpiresAt.UtcDateTime.AddDays(1))
        );

        Assert.Equal(VoucherSpendability.Revoked, spendability);
    }

    [Fact]
    public void CheckSpendability_WithAllowanceRemaining_IsSpendable()
    {
        Voucher voucher = IssuedVoucher();
        voucher.RecordConsumption(Allowance - 1);

        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(IssuedAt)
        );

        Assert.Equal(VoucherSpendability.Spendable, spendability);
    }

    [Fact]
    public void CheckSpendability_WithConsumptionExactlyAtAllowance_IsExhausted()
    {
        Voucher voucher = IssuedVoucher();
        voucher.RecordConsumption(Allowance);

        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(IssuedAt)
        );

        Assert.Equal(VoucherSpendability.Exhausted, spendability);
    }

    [Fact]
    public void CheckSpendability_WithConsumptionOverAllowance_IsExhausted()
    {
        Voucher voucher = IssuedVoucher();

        // The overshoot a soft budget permits: the request that crossed the line was
        // already served, and the next one is what gets turned away.
        voucher.RecordConsumption(Allowance + 5_000);

        VoucherSpendability spendability = voucher.CheckSpendability(
            new FakeDateTimeProvider(IssuedAt)
        );

        Assert.Equal(VoucherSpendability.Exhausted, spendability);
    }

    [Fact]
    public void CheckSpendability_WhenExpiredAndExhausted_ReportsExpired()
    {
        Voucher voucher = IssuedVoucher();
        voucher.RecordConsumption(Allowance);

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
