using System.ComponentModel.DataAnnotations;

namespace LlmVoucherGateway.Api.Persistence;

/// <summary>
/// Where the voucher database lives.
/// </summary>
internal sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Path to the SQLite file. A relative path resolves against the process working
    /// directory, and in a container this points at the mounted volume so the file
    /// outlives the container.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Path { get; set; } = string.Empty;
}
