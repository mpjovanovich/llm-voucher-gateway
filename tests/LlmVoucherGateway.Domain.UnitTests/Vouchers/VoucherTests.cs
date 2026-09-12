using LlmVoucherGateway.Domain.Abstractions;
using LlmVoucherGateway.Domain.Vouchers;

namespace LlmVoucherGateway.Domain.UnitTests.Vouchers;

public class VoucherTests
{
    private sealed class FakeDateTimeProvider(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TheoryData<DateTimeOffset> NonFutureExpiryTimes() =>
        new()
        {
            new DateTimeOffset(FixedUtcNow),
            new DateTimeOffset(FixedUtcNow).AddDays(-1),
        };

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Issue_WithZeroOrNegativeAllowance_ThrowsArgumentOutOfRangeException(int allowance)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            Voucher.Issue("section", allowance, FixedUtcNow.AddDays(1), new FakeDateTimeProvider(FixedUtcNow)));

        Assert.Equal(nameof(allowance), exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NonFutureExpiryTimes))]
    public void Issue_WithExpiresAtEqualToOrBeforeCreatedAt_ThrowsArgumentOutOfRangeException(DateTimeOffset expiresAt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Voucher.Issue("section", 100, expiresAt, new FakeDateTimeProvider(FixedUtcNow)));
    }

    [Fact]
    public void Issue_WithValidArguments_InitializesConsumedToZeroAndStatusActive()
    {
        IssuedVoucher issued = Voucher.Issue("section", 100, FixedUtcNow.AddDays(1), new FakeDateTimeProvider(FixedUtcNow));

        Assert.Equal(0, issued.Voucher.Consumed);
        Assert.Equal(VoucherStatus.Active, issued.Voucher.Status);
    }

    [Fact]
    public void Issue_CalledMultipleTimes_GeneratesUniqueKeysAndIds()
    {
        FakeDateTimeProvider dateTimeProvider = new(FixedUtcNow);

        IssuedVoucher first = Voucher.Issue("section", 100, FixedUtcNow.AddDays(1), dateTimeProvider);
        IssuedVoucher second = Voucher.Issue("section", 100, FixedUtcNow.AddDays(1), dateTimeProvider);

        Assert.NotEqual(first.Voucher.Id, second.Voucher.Id);
        Assert.NotEqual(first.PlaintextKey, second.PlaintextKey);
        Assert.NotEqual(first.Voucher.Key.Hash, second.Voucher.Key.Hash);
    }
}
