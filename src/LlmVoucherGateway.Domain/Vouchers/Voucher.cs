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
        // Ordered by how deliberate each reason is, so the holder is told the most
        // actionable one first.
        if (Status == VoucherStatus.Revoked)
        {
            return VoucherSpendability.Revoked;
        }

        // Expired at the instant of expiry, not after it: the voucher is valid up to
        // that point and not including it.
        if (dateTimeProvider.UtcNow >= Lifetime.ExpiresAt)
        {
            return VoucherSpendability.Expired;
        }

        // Recording happens after a request, so Consumed can land above Allowance and
        // the comparison has to be "at or over" rather than "over".
        if (Consumed >= Allowance)
        {
            return VoucherSpendability.Exhausted;
        }

        return VoucherSpendability.Spendable;
    }

    // Idempotent: revoking a revoked voucher already satisfies the caller's intent. The
    // endpoint and the recorded reason are Milestone 8; the transition lives here because
    // the rule that reads it does.
    public void Revoke()
    {
        Status = VoucherStatus.Revoked;
    }

    // Accepts a total above the allowance. The budget is soft by design, and refusing to
    // record an overshoot would lose the evidence of it.
    public void RecordConsumption(int tokens)
    {
        if (tokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tokens),
                tokens,
                VoucherErrors.ConsumedTokensMustNotBeNegative
            );
        }

        Consumed += tokens;
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
