namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Counts every byte read from a response's wire stream into a fetch's usage, before any decoder sees it.</summary>
/// <remarks>Reading through this stream is what makes <see cref="FetchUsageCounters.Bytes"/> the bytes a host or proxy sent,
/// whatever <c>Content-Encoding</c> the body used. It also keeps the last eight bytes read, where a gzip stream holds its trailer.</remarks>
internal sealed class MeteredStream(Stream inner, FetchUsageCounters usage) : Stream
{
    private readonly byte[] tail = new byte[8];

    /// <summary>Gets how many bytes this stream has read.</summary>
    internal long Total { get; private set; }

    /// <summary>Gets the last eight bytes read, the earlier ones zero while fewer have been read.</summary>
    internal ReadOnlySpan<byte> Tail => tail;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        Count(buffer[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken);
        Count(buffer.Span[..read]);
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask DisposeAsync() => inner.DisposeAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    private void Count(ReadOnlySpan<byte> read)
    {
        usage.Bytes += read.Length;
        Total += read.Length;
        if (read.Length >= tail.Length)
        {
            read[^tail.Length..].CopyTo(tail);
        }
        else if (read.Length > 0)
        {
            tail.AsSpan(read.Length).CopyTo(tail);
            read.CopyTo(tail.AsSpan(tail.Length - read.Length));
        }
    }
}
