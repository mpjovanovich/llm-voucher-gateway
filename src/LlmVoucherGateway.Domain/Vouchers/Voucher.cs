using LlmVoucherGateway.Domain.Abstractions;

namespace LlmVoucherGateway.Domain.Vouchers;

public sealed class Voucher : Entity
{
    private Voucher(
        Guid id,
        VoucherKey key,
        string? section,
        int allowance,
        VoucherLifetime lifetime
    )
        : base(id)
    {
        Key = key;
        Section = section;
        Allowance = allowance;
        Consumed = 0;
        Status = VoucherStatus.Active;
        Lifetime = lifetime;
    }

    public VoucherKey Key { get; private set; }
    public string? Section { get; set; }
    public int Allowance { get; private set; }
    public int Consumed { get; private set; }
    public VoucherStatus Status { get; private set; }
    public VoucherLifetime Lifetime { get; private set; }

    public static IssuedVoucher Issue(
        string? section, 
        int allowance, 
        DateTimeOffset expiresAt, 
        IDateTimeProvider dateTimeProvider
    )
    {
        if (allowance <= 0)
            throw new ArgumentOutOfRangeException(nameof(allowance), allowance, VoucherErrors.AllowanceMustBePositive);

        GeneratedVoucherKey generatedKey = VoucherKeyGenerator.Generate();

        VoucherLifetime lifetime = new(dateTimeProvider.UtcNow, expiresAt);

        Voucher voucher = new(Guid.NewGuid(), generatedKey.Key, section, allowance, lifetime);

        return new IssuedVoucher(voucher, generatedKey.PlaintextKey);
    }
}
