using System.Buffers;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Reads usage from a single JSON response by keeping a copy of the body as it is relayed.
/// </summary>
/// <remarks>
/// The copy is a side buffer, not a delay: the caller's bytes are written as they arrive.
/// A non-streaming body is one completion's worth of JSON, so holding it is cheap.
/// </remarks>
internal sealed class BufferedUsageObserver : IUsageObserver
{
    private readonly ArrayBufferWriter<byte> _body = new();

    public void Observe(ReadOnlySpan<byte> bytes) => _body.Write(bytes);

    public int? ReadTotalTokens() => TokenUsage.ReadTotalTokens(_body.WrittenMemory);
}
