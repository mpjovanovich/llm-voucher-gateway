using System.ComponentModel.DataAnnotations;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Configuration for the upstream inference server the gateway forwards to.
/// </summary>
internal sealed class InferenceOptions
{
    public const string SectionName = "Inference";

    /// <summary>
    /// Root address of the inference server, e.g. <c>http://localhost:8000</c>.
    /// Request paths are appended to this, so it carries no path of its own.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string BaseUrl { get; set; } = string.Empty;
}
