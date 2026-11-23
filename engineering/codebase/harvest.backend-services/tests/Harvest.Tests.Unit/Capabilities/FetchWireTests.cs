using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>Runs the fetch handler over real sockets against a local HTTP/1.1 server, so the encodings, the wire meter and the
/// connection pool are the runtime's own, not a script's.</summary>
public sealed class FetchWireTests
{
    private static readonly byte[] Page = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 3000).Select(index => $"<li data-id=\"{index}\">Listing {index}</li>\n")));
    private static readonly byte[] Robots = Encoding.UTF8.GetBytes("User-agent: *\nAllow: /\n");

    [Fact]
    public async Task Fetch_ShouldDecodeGzipBrotliChunkedAndPlainBodies_AndReuseOneConnection()
    {
        byte[] gzip = Encode(Page, "gzip");
        byte[] brotli = Encode(Page, "br");
        byte[] robots = Encode(Robots, "gzip");
        await using var server = new WireServer(path => path switch
        {
            "/robots.txt" => Reply(robots, "gzip"),
            "/gzip" => Reply(gzip, "gzip"),
            "/brotli-chunked" => Reply(brotli, "br", chunked: true),
            "/gzip-chunked" => Reply(gzip, "gzip", chunked: true),
            _ => Reply(Page, null),
        });
        var host = new FetchTestHost();
        using var client = new HttpClient(new SocketsHttpHandler
        {
            UseCookies = false, AllowAutoRedirect = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None,
        }) { Timeout = Timeout.InfiniteTimeSpan };
        host.TransportFactory = new SharedClientRoutes(client);

        var first = await host.Fetcher.FetchAsync(Request(server, "/gzip") with { RobotsPolicy = RobotsPolicy.Obey });
        var second = await host.Fetcher.FetchAsync(Request(server, "/brotli-chunked"));
        var third = await host.Fetcher.FetchAsync(Request(server, "/plain"));
        var fourth = await host.Fetcher.FetchAsync(Request(server, "/gzip-chunked"));

        foreach (var result in new[] { first, second, third, fourth })
        {
            result.Outcome.Should().Be(FetchOutcome.Ok);
            result.Document!.Body.ToArray().Should().Equal(Page);
            result.Document.Headers.Select(header => header.Key).Should().NotContain("Content-Encoding");
        }

        first.Usage.Requests.Should().Be(2);
        first.Usage.Bytes.Should().Be(robots.Length + gzip.Length, "the meter counts the compressed bytes of robots and the page");
        second.Usage.Bytes.Should().Be(brotli.Length);
        third.Usage.Bytes.Should().Be(Page.Length);
        fourth.Usage.Bytes.Should().Be(gzip.Length);
        server.Requests.Should().HaveCount(5).And.OnlyContain(request => request.AcceptEncoding == "gzip, br");
        server.Connections.Should().Be(1, "each body is read to its end, so the pooled connection is reused");
    }

    [Fact]
    public async Task Fetch_ShouldFailAsDecodeError_AndKeepServingOnTheNextFetch_WhenABodyIsCorruptOverTheWire()
    {
        byte[] gzip = Encode(Page, "gzip");
        byte[] corrupt = [.. gzip.Select((value, index) => index < 12 ? value : (byte)(value ^ 0x5a))];
        await using var server = new WireServer(path => path == "/corrupt" ? Reply(corrupt, "gzip") : Reply(gzip, "gzip"));
        var host = new FetchTestHost();
        using var client = new HttpClient(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = false, UseProxy = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        host.TransportFactory = new SharedClientRoutes(client);

        var bad = await host.Fetcher.FetchAsync(Request(server, "/corrupt"));
        var good = await host.Fetcher.FetchAsync(Request(server, "/ok"));

        bad.Outcome.Should().Be(FetchOutcome.DecodeError);
        host.Hosts.State.PausedUntil.Should().BeNull();
        good.Outcome.Should().Be(FetchOutcome.Ok);
        good.Document!.Body.ToArray().Should().Equal(Page);
    }

    [Fact]
    public async Task Fetch_ShouldAbandonTheUnreadGzipTail_AndStillReadDetailsInFull()
    {
        byte[] prefix = [.. Page, .. "</catalog-prefix>"u8];
        using var compressed = new MemoryStream();
        int prefixWireBytes;
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(prefix);
            gzip.Flush();
            prefixWireBytes = (int)compressed.Length;
            gzip.Write(Page);
        }
        byte[] gzipBody = compressed.ToArray();
        byte[] full = Reply(gzipBody, "gzip");
        byte[] stalled = full[..(full.Length - gzipBody.Length + prefixWireBytes)];

        // Leave the advertised tail unsent. Ending the prefix read must close the socket without waiting for it.
        await using var server = new WireServer(path => path == "/catalog" ? stalled : full);
        var host = new FetchTestHost();
        using var client = new HttpClient(new SocketsHttpHandler
        {
            UseCookies = false, AllowAutoRedirect = false, UseProxy = false,
            MaxResponseDrainSize = 0, ResponseDrainTimeout = TimeSpan.Zero,
        }) { Timeout = Timeout.InfiniteTimeSpan };
        host.TransportFactory = new SharedClientRoutes(client, supportsEarlyBodyStop: true);
        var prefixPolicy = new CatalogPrefixPolicy();

        var catalog = await host.Fetcher.FetchAsync(Request(server, "/catalog") with { HtmlPrefixPolicy = prefixPolicy });
        catalog.Outcome.Should().Be(FetchOutcome.Ok);
        catalog.Document!.IsBodyPrefix.Should().BeTrue();
        catalog.Document.Body.ToArray().Should().Equal(prefix);
        catalog.Usage.Bytes.Should().Be(prefixWireBytes).And.BeLessThan(gzipBody.Length);
        await server.Disconnected.WaitAsync(TimeSpan.FromSeconds(1.5));

        var detail = await host.Fetcher.FetchAsync(Request(server, "/detail") with { HtmlPrefixPolicy = prefixPolicy });
        detail.Outcome.Should().Be(FetchOutcome.Ok);
        detail.Document!.IsBodyPrefix.Should().BeFalse();
        detail.Document.Body.ToArray().Should().Equal([.. prefix, .. Page]);
        detail.Usage.Bytes.Should().Be(gzipBody.Length);
        server.Requests.Select(request => request.Path).Should().Equal("/catalog", "/detail");
    }

    private sealed class CatalogPrefixPolicy : IFetchHtmlPrefixPolicy
    {
        public bool AllowsPrefix(Uri destination) => destination.AbsolutePath == "/catalog";
        public bool ShouldStopReading(Uri destination, ReadOnlySpan<byte> prefix, int previousLength) =>
            prefix.IndexOf("</catalog-prefix>"u8) >= 0;
    }

    private static FetchRequest Request(WireServer server, string path) =>
        new() { Uri = new Uri($"http://127.0.0.1:{server.Port}{path}"), RobotsPolicy = RobotsPolicy.Ignore };

    private static byte[] Encode(byte[] body, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream encoder = encoding == "gzip" ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
            : new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            encoder.Write(body);
        return output.ToArray();
    }

    /// <summary>Builds an HTTP/1.1 reply: the body as a sized one or in two chunks, with its <c>Content-Encoding</c> when it has one.</summary>
    private static byte[] Reply(byte[] body, string? encoding, bool chunked = false)
    {
        using var reply = new MemoryStream();
        var head = new StringBuilder("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n");
        if (encoding is not null)
            head.Append("Content-Encoding: ").Append(encoding).Append("\r\n");
        head.Append(chunked ? "Transfer-Encoding: chunked\r\n\r\n" : $"Content-Length: {body.Length}\r\n\r\n");
        reply.Write(Encoding.ASCII.GetBytes(head.ToString()));
        if (!chunked)
        {
            reply.Write(body);
        }
        else
        {
            int split = body.Length / 2;
            foreach (byte[] part in new[] { body[..split], body[split..] })
            {
                reply.Write(Encoding.ASCII.GetBytes($"{part.Length:x}\r\n"));
                reply.Write(part);
                reply.Write("\r\n"u8);
            }

            reply.Write("0\r\n\r\n"u8);
        }

        return reply.ToArray();
    }

    /// <summary>Reaches a loopback server through one client that outlives each fetch's lease, as a shared proxy client does.</summary>
    private sealed class SharedClientRoutes(HttpClient client, bool supportsEarlyBodyStop = false) : IFetchTransportFactory
    {
        public ValueTask<IFetchTransportLease> AcquireAsync(Uri destination, CookieContainer? runCookies = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IFetchTransportLease>(new Route(client, destination, supportsEarlyBodyStop));
    }

    private sealed class Route(HttpClient client, Uri origin, bool supportsEarlyBodyStop) : IFetchTransportLease
    {
        public HttpClient Client => client;
        public CookieContainer? Cookies => null;
        public bool IsPrivate => true;
        public string RouteKey => "wire";
        public bool SupportsEarlyBodyStop => supportsEarlyBodyStop;
        public bool Allows(Uri destination) => destination.Authority == origin.Authority;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Answers each request of a keep-alive connection with the bytes the script gives for its path.</summary>
    private sealed class WireServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Func<string, byte[]> respond;
        private readonly Task accepting;
        private readonly TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int connections;

        internal WireServer(Func<string, byte[]> respond)
        {
            this.respond = respond;
            listener.Start();
            accepting = Task.Run(AcceptAsync);
        }

        internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        internal int Connections => Volatile.Read(ref connections);
        internal Task Disconnected => disconnected.Task;
        internal ConcurrentQueue<(string Path, string? AcceptEncoding)> Requests { get; } = [];

        private async Task AcceptAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient connection = await listener.AcceptTcpClientAsync(stop.Token);
                    Interlocked.Increment(ref connections);
                    _ = Task.Run(() => ServeAsync(connection));
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ServeAsync(TcpClient connection)
        {
            using (connection)
            {
                try
                {
                    NetworkStream stream = connection.GetStream();
                    var head = new List<byte>();
                    byte[] chunk = new byte[4096];
                    while (!stop.IsCancellationRequested)
                    {
                        int read = await stream.ReadAsync(chunk, stop.Token);
                        if (read == 0)
                        {
                            disconnected.TrySetResult();
                            return;
                        }
                        head.AddRange(chunk.AsSpan(0, read).ToArray());
                        string text = Encoding.ASCII.GetString([.. head]);
                        int end;
                        while ((end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal)) >= 0)
                        {
                            string[] lines = text[..end].Split("\r\n");
                            string path = lines[0].Split(' ')[1];
                            string? accept = lines.Skip(1).Select(line => line.Split(": ", 2)).FirstOrDefault(pair => pair[0].Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase))?[1];
                            Requests.Enqueue((path, accept));
                            await stream.WriteAsync(respond(path), stop.Token);
                            text = text[(end + 4)..];
                        }

                        head = [.. Encoding.ASCII.GetBytes(text)];
                    }
                }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await stop.CancelAsync();
            listener.Stop();
            await accepting;
            stop.Dispose();
        }
    }
}
