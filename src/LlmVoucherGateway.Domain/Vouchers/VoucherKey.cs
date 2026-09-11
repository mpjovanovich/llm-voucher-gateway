namespace LlmVoucherGateway.Domain.Vouchers;

public sealed record VoucherKey
{
    public VoucherKey(string hash, string prefix)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new ArgumentException(VoucherErrors.KeyHashRequired, nameof(hash));

        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException(VoucherErrors.KeyPrefixRequired, nameof(prefix));

        Hash = hash;
        Prefix = prefix;
    }

    public string Hash { get; }

    public string Prefix { get; }
}
