using System.Buffers;

namespace LlmVoucherGateway.Api.Inference;

/// <summary>
/// Reads usage from a server-sent event stream by remembering only the last <c>data:</c>
/// payload and parsing it once, at the end.
/// </summary>
/// <remarks>
/// That is enough because vLLM runs with <c>--enable-force-include-usage</c>, which puts a
/// running total on every chunk. It also means a stream cut short by a disconnect still
/// reports what was generated up to the last chunk that got out.
/// </remarks>
internal sealed class StreamedUsageObserver : IUsageObserver
{
    private readonly ArrayBufferWriter<byte> _partialLine = new();

    private byte[]? _lastData;

    private static ReadOnlySpan<byte> DataField => "data:"u8;

    private static ReadOnlySpan<byte> DoneSentinel => "[DONE]"u8;

    public void Observe(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            int newline = bytes.IndexOf((byte)'\n');

            // A read can end mid-line, so the start of the line is held until the rest
            // arrives. An unterminated line at the very end is never used: it is the
            // fragment a disconnect leaves behind.
            if (newline < 0)
            {
                _partialLine.Write(bytes);
                return;
            }

            if (_partialLine.WrittenCount > 0)
            {
                _partialLine.Write(bytes[..newline]);
                ObserveLine(_partialLine.WrittenSpan);
                _partialLine.ResetWrittenCount();
            }
            else
            {
                ObserveLine(bytes[..newline]);
            }

            bytes = bytes[(newline + 1)..];
        }
    }

    public int? ReadTotalTokens() =>
        _lastData is null ? null : TokenUsage.ReadTotalTokens(_lastData);

    private void ObserveLine(ReadOnlySpan<byte> line)
    {
        line = line.TrimEnd((byte)'\r');

        if (!line.StartsWith(DataField))
        {
            return;
        }

        ReadOnlySpan<byte> data = line[DataField.Length..].TrimStart((byte)' ');

        if (data.SequenceEqual(DoneSentinel))
        {
            return;
        }

        _lastData = data.ToArray();
    }
}
