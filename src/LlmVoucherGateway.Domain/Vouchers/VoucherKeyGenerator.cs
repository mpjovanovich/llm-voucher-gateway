using System.Security.Cryptography;

namespace LlmVoucherGateway.Domain.Vouchers;

internal static class VoucherKeyGenerator
{
    private const int SecretByteLength = 32;
    private const int PrefixLength = 8;

    public static GeneratedVoucherKey Generate()
    {
        string plaintextValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(SecretByteLength)).ToLowerInvariant();
        string hash = VoucherKey.ComputeHash(plaintextValue);
        string prefix = plaintextValue[..PrefixLength];

        return new GeneratedVoucherKey(new VoucherKey(hash, prefix), plaintextValue);
    }
}
