using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Net.Http.Headers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Decodes a response body whose <c>Content-Encoding</c> is <c>gzip</c> or <c>br</c>, the two a fetch asks for, and
/// says whether the encoded stream ended where it should.</summary>
/// <remarks>The runtime's decoders return what they have when a stream is cut short, which would store a partial page, so the
/// decoder checks the end itself: a Brotli stream must reach its last block (<see cref="BrotliDecodingStream"/>) and a gzip stream's
/// trailer must hold the decoded length, with the runtime's own check of the gzip checksum. A gzip body of several members is
/// therefore a decode error.</remarks>
internal sealed class ContentDecoder : IAsyncDisposable
{
    private const int GzipTrailerBytes = 8;
    private const int GzipMinimumBytes = 18;
    private readonly MeteredStream wire;
    private readonly bool gzip;

    private ContentDecoder(Stream stream, MeteredStream wire, bool gzip)
    {
        Stream = stream;
        this.wire = wire;
        this.gzip = gzip;
    }

    /// <summary>The <c>Accept-Encoding</c> tokens every harvest fetch sends, robots included.</summary>
    internal static readonly string[] Requested = ["gzip", "br"];

    /// <summary>Gets the stream the decoded body is read from.</summary>
    internal Stream Stream { get; }

    /// <summary>Opens the decoder a response's encoding calls for.</summary>
    /// <param name="headers">The response's content headers.</param>
    /// <param name="wire">The raw, metered response stream; the decoder leaves it open.</param>
    /// <param name="decoder">The decoder, or null when no encoding or <c>identity</c> is named: the caller reads <paramref name="wire"/>.</param>
    /// <returns>False for any other encoding, or a stacked list of encodings.</returns>
    internal static bool TryOpen(HttpContentHeaders headers, MeteredStream wire, out ContentDecoder? decoder)
    {
        string[] encodings = [.. headers.ContentEncoding];
        decoder = null;
        if (encodings.Length == 0 || encodings is [var only] && only.Equals("identity", StringComparison.OrdinalIgnoreCase))
            return true;
        if (encodings is not [var encoding])
            return false;
        if (encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            decoder = new(new GZipStream(wire, CompressionMode.Decompress, leaveOpen: true), wire, gzip: true);
        else if (encoding.Equals("br", StringComparison.OrdinalIgnoreCase))
            decoder = new(new BrotliDecodingStream(wire), wire, gzip: false);
        else
            return false;
        return true;
    }

    /// <summary>Checks, once the wire is read to its end, that the encoded stream was whole.</summary>
    /// <param name="decodedBytes">How many decoded bytes were read.</param>
    internal bool IsComplete(long decodedBytes)
    {
        if (!gzip)
            return true;
        if (wire.Total == 0)
            return decodedBytes == 0;
        return wire.Total >= GzipMinimumBytes
            && BinaryPrimitives.ReadUInt32LittleEndian(wire.Tail[(GzipTrailerBytes - sizeof(uint))..]) == (uint)decodedBytes;
    }

    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}

/// <summary>Reads a Brotli stream through the decoder's own status, so a stream cut short fails instead of ending quietly.</summary>
internal sealed class BrotliDecodingStream(Stream wire) : Stream
{
    private readonly byte[] input = new byte[16 * 1024];
    private BrotliDecoder decoder;
    private int consumed;
    private int available;
    private bool started;
    private bool finished;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (!finished && !buffer.IsEmpty)
        {
            // The decoder holds output of its own, so it is asked again before more input is read.
            OperationStatus status = decoder.Decompress(input.AsSpan(consumed, available - consumed), buffer.Span, out int used, out int written);
            consumed += used;
            switch (status)
            {
                case OperationStatus.Done:
                    finished = true;
                    return written;
                case OperationStatus.InvalidData:
                    throw new InvalidDataException("The Brotli stream is corrupt.");
                case OperationStatus.DestinationTooSmall when written == 0 && used == 0:
                    throw new InvalidDataException("The Brotli decoder made no progress.");
            }

            if (written > 0)
                return written;
            if (status != OperationStatus.NeedMoreData)
                continue;
            available = await wire.ReadAsync(input, cancellationToken);
            consumed = 0;
            if (available == 0)
            {
                // A body with nothing on the wire is an empty body; a stream that ends mid-way is not.
                if (started)
                    throw new InvalidDataException("The Brotli stream ended before its last block.");
                finished = true;
            }
            started = true;
        }

        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            decoder.Dispose();
        base.Dispose(disposing);
    }
}
