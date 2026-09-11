namespace LlmVoucherGateway.Domain.Vouchers;

public sealed record VoucherLifetime
{
    public VoucherLifetime(DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        if (expiresAt <= createdAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), expiresAt, VoucherErrors.ExpiryMustBeInFuture);

        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }
}
