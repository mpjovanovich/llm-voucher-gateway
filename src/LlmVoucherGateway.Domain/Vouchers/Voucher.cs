using LlmVoucherGateway.Domain.Abstractions;

namespace LlmVoucherGateway.Domain.Vouchers;

public sealed class Voucher : Entity
{
    private Voucher(
        Guid id,
        VoucherKey key,
        string section,
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

    public static Voucher Issue(
        VoucherKey key, 
        VoucherLifetime lifetime,
        string? section, 
        int allowance,
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(lifetime);

        if (allowance <= 0)
            throw new ArgumentOutOfRangeException(nameof(allowance), allowance, VoucherErrors.AllowanceMustBePositive);

        return new Voucher(Guid.NewGuid(), key, section, allowance, lifetime);
    }
}
