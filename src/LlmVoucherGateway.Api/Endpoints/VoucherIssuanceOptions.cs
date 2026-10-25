using System.ComponentModel.DataAnnotations;

namespace LlmVoucherGateway.Api.Endpoints;

/// <summary>
/// Limits on a single issuance call.
/// </summary>
internal sealed class VoucherIssuanceOptions
{
    public const string SectionName = "VoucherIssuance";

    /// <summary>
    /// Generous for a classroom batch and small enough that a mistyped count cannot
    /// mint a runaway batch, which matters because every key in the response is
    /// unrecoverable once the response is gone.
    /// </summary>
    public const int DefaultMaxPerRequest = 100;

    [Range(1, int.MaxValue, ErrorMessage = "MaxPerRequest must be positive.")]
    public int MaxPerRequest { get; set; } = DefaultMaxPerRequest;
}
