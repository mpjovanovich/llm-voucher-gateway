using System.ComponentModel.DataAnnotations;

namespace LlmVoucherGateway.Api.Credentials;

/// <summary>
/// Bearer tokens that identify an administrator. Any match grants the same access;
/// the list exists so a key can be rotated by adding the new one before removing the old.
/// </summary>
internal sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Plaintext admin keys, compared against the bearer token as-is. These are
    /// operator-managed secrets, not issued vouchers, so nothing here is hashed.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one admin key must be configured.")]
    public string[] Keys { get; set; } = [];
}
