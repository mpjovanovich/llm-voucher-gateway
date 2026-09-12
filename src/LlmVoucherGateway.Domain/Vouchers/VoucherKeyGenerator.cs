using System.Security.Cryptography;
using System.Text;

namespace LlmVoucherGateway.Domain.Vouchers;

public static class VoucherKeyGenerator
{
    private const int SecretByteLength = 32;
    private const int PrefixLength = 8;

    public static GeneratedVoucherKey Generate()
    {
        string plaintextValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretByteLength)).ToLowerInvariant();
        string hash = Hash(plaintextValue);
        string prefix = plaintextValue[..PrefixLength];

        return new GeneratedVoucherKey(new VoucherKey(hash, prefix), plaintextValue);
    }

    public static string Hash(string plaintextValue)
    {
        if (string.IsNullOrWhiteSpace(plaintextValue))
            throw new ArgumentException(VoucherErrors.PlaintextValueRequired, nameof(plaintextValue));

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextValue));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
