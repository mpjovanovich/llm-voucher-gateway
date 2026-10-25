using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Domain.UnitTests.Vouchers;

public class VoucherConsumptionTests
{
    private static readonly DateTime IssuedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Voucher IssuedVoucher() =>
        Voucher.Issue(
            "CSCI-101",
            allowance: 100_000,
            new DateTimeOffset(IssuedAt).AddDays(30),
            new FakeDateTimeProvider(IssuedAt)
        ).Voucher;

    [Fact]
    public void RecordConsumption_Accumulates()
    {
        Voucher voucher = IssuedVoucher();

        voucher.RecordConsumption(1_000);
        voucher.RecordConsumption(500);

        Assert.Equal(1_500, voucher.Consumed);
    }

    [Fact]
    public void RecordConsumption_WithZero_LeavesConsumedUnchanged()
    {
        Voucher voucher = IssuedVoucher();

        // A request that produced no tokens still gets recorded rather than rejected.
        voucher.RecordConsumption(0);

        Assert.Equal(0, voucher.Consumed);
    }

    [Fact]
    public void RecordConsumption_WithNegativeTokens_ThrowsArgumentOutOfRangeException()
    {
        Voucher voucher = IssuedVoucher();

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => voucher.RecordConsumption(-1)
        );

        Assert.Equal("tokens", exception.ParamName);
    }
}
