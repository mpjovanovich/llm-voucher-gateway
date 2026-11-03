using System.Text;

namespace LlmVoucherGateway.Api.IntegrationTests.Inference;

/// <summary>
/// An upstream body that hands over its first chunk immediately and holds the rest back
/// until <see cref="Release"/> is called.
/// </summary>
/// <remarks>
/// A fully materialized stream cannot show whether a relay forwards or buffers: all of its
/// bytes are available at once, so both behaviours look identical from the caller's end.
/// Withholding the later chunks is what makes the difference observable — while they are
/// held, anything the caller has received must have been forwarded rather than accumulated.
/// </remarks>
internal sealed class ReleasedChunkStream : Stream
{
    private readonly IReadOnlyList<byte[]> chunks;

    private readonly TaskCompletionSource released =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int chunkIndex;
    private int offsetInChunk;

    public ReleasedChunkStream(params string[] chunks)
    {
        this.chunks = [.. chunks.Select(Encoding.UTF8.GetBytes)];
    }

    /// <summary>
    /// Lets every chunk after the first through.
    /// </summary>
    public void Release() => released.TrySetResult();

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        if (chunkIndex >= chunks.Count)
        {
            return 0;
        }

        // Only the first chunk is free. Until the test releases the rest, a read that
        // wants them blocks here — which is precisely the state in which the caller's
        // received bytes are worth asserting on.
        if (chunkIndex > 0)
        {
            await released.Task.WaitAsync(cancellationToken);
        }

        byte[] chunk = chunks[chunkIndex];

        // Honour a buffer smaller than the chunk rather than assuming one big enough:
        // the copy buffer size is the caller's business, not this stream's.
        int count = Math.Min(chunk.Length - offsetInChunk, buffer.Length);
        chunk.AsMemory(offsetInChunk, count).CopyTo(buffer);

        offsetInChunk += count;

        if (offsetInChunk == chunk.Length)
        {
            chunkIndex++;
            offsetInChunk = 0;
        }

        return count;
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        // Nothing is written to this stream, so there is nothing to flush.
    }

    // The relay copies asynchronously, so the synchronous path is never taken. Throwing
    // rather than implementing it keeps a future synchronous read from quietly working
    // and defeating the point of the gate above.
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}
