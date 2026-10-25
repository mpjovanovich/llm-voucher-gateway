using System.Security.Cryptography;
using System.Text;

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

    // Public because resolving a bearer token means hashing it and matching this column,
    // which happens outside the domain. One implementation, so a token hashed at
    // issuance and a token hashed at request time cannot diverge.
    public static string ComputeHash(string plaintextValue)
    {
        if (string.IsNullOrWhiteSpace(plaintextValue))
        {
            throw new ArgumentException(VoucherErrors.PlaintextValueRequired, nameof(plaintextValue));
        }

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextValue));

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
