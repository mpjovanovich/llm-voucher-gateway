using System.Text.Json;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Reads the token count out of an upstream payload — a whole non-streaming response or a
/// single streamed chunk. The only part of a response the gateway ever parses.
/// </summary>
internal static class TokenUsage
{
    /// <summary>
    /// <c>usage.total_tokens</c>, or <c>null</c> when the payload has no readable count.
    /// </summary>
    /// <remarks>
    /// <c>total_tokens</c> already includes the prompt, so it is read rather than computed;
    /// adding <c>prompt_tokens</c> to it would bill the prompt twice.
    /// </remarks>
    public static int? ReadTotalTokens(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (
                // If everything is valid for the provided json, return the total tokens.
                // This is validating shape and type.
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("usage", out JsonElement usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("total_tokens", out JsonElement totalTokens)
                && totalTokens.ValueKind == JsonValueKind.Number
                && totalTokens.TryGetInt32(out int total)
                && total >= 0
            )
            {
                return total;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
