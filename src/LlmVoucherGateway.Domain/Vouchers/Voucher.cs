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

    private Voucher()
    {
        // Required by EF Core. It constructs the instance first and then sets the owned
        // value objects through their properties, because an owned type cannot be bound
        // to a constructor parameter. The null-forgiving assignments stand in for values
        // EF supplies immediately afterwards.
        Key = null!;
        Lifetime = null!;
    }

    public VoucherKey Key { get; private set; }
    public string? Section { get; set; }
    public int Allowance { get; private set; }
    public int Consumed { get; private set; }
    public VoucherStatus Status { get; private set; }
    public VoucherLifetime Lifetime { get; private set; }

    /// <summary>
    /// Whether this voucher may be spent right now. Takes the clock rather than reading
    /// it, so the rule is testable at the instant either side of a boundary.
    /// </summary>
    public VoucherSpendability CheckSpendability(IDateTimeProvider dateTimeProvider)
    {
        // Expired at the instant of expiry, not after it: the voucher is valid up to
        // that point and not including it.
        if (dateTimeProvider.UtcNow >= Lifetime.ExpiresAt)
        {
            return VoucherSpendability.Expired;
        }

        return VoucherSpendability.Spendable;
    }

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
