using System.IO.Compression;
using System.Net;
using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>Every harvest fetch asks for <c>gzip</c> and <c>br</c>, meters the bytes on the wire, and reads the decoded body under the
/// size limit; the downstream sees the bytes an unencoded reply would have carried.</summary>
public sealed class FetchEncodingTests
{
    [Theory]
    [InlineData("gzip", false)]
    [InlineData("br", false)]
    [InlineData("gzip", true)]
    [InlineData("br", true)]
    public async Task Fetch_ShouldDecodeToTheIdentityBytesAndMeterTheCompressedLength_WhenTheBodyIsEncoded(string encoding, bool proxy)
    {
        var host = Create(proxy);
        byte[] page = Page(200);
        byte[] wire = Encode(page, encoding);
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(wire, encoding));

        var result = await host.Fetcher.FetchAsync(Request());

        wire.Length.Should().BeLessThan(page.Length / 4);
        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.Body.ToArray().Should().Equal(page);
        result.Usage.Requests.Should().Be(1);
        result.Usage.Bytes.Should().Be(wire.Length);
        result.Document.Headers.Select(header => header.Key).Should().NotContain("Content-Encoding");
        result.Document.Headers.Where(header => header.Key == "Content-Length").Select(header => header.Value)
            .Should().OnlyContain(value => value == page.Length.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fetch_ShouldDecodeAndMeterRobotsAndThePage_WhenBothAreEncoded(bool proxy)
    {
        var host = Create(proxy);
        byte[] robots = Encoding.UTF8.GetBytes("User-agent: *\nDisallow: /private\n");
        byte[] page = Page(60);
        byte[] robotsWire = Encode(robots, "gzip");
        byte[] pageWire = Encode(page, "br");
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(robotsWire, "gzip"));
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(pageWire, "br"));

        var allowed = await host.Fetcher.FetchAsync(Request() with { RobotsPolicy = RobotsPolicy.Obey });
        if (proxy)
            host.Transport.Responses.Enqueue(FetchTestHost.Encoded(robotsWire, "gzip")); // A private route reads robots again.
        var refused = await host.Fetcher.FetchAsync(Request() with { Uri = new Uri("https://example.com/private/x"), RobotsPolicy = RobotsPolicy.Obey });

        allowed.Outcome.Should().Be(FetchOutcome.Ok);
        allowed.Document!.Body.ToArray().Should().Equal(page);
        allowed.Usage.Requests.Should().Be(2);
        allowed.Usage.Bytes.Should().Be(robotsWire.Length + pageWire.Length);
        refused.Outcome.Should().Be(FetchOutcome.RobotsDisallowed, "the rules were decoded before they were parsed");
        refused.Usage.Bytes.Should().Be(proxy ? robotsWire.Length : 0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Fetch_ShouldSendAcceptEncodingGzipAndBrotliOnEveryRequest_RobotsAndRedirectsIncluded(bool proxy, bool paced)
    {
        var host = Create(proxy);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
        var redirect = FetchTestHost.Response(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("/final", UriKind.Relative);
        host.Transport.Responses.Enqueue(redirect);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "final"));
        var request = Request(paced) with { RobotsPolicy = RobotsPolicy.Obey };
        var result = await host.Fetcher.FetchAsync(request);

        result.Outcome.Should().Be(FetchOutcome.Ok);
        host.Transport.Requests.Select(item => item.Uri.AbsolutePath).Should().Equal("/robots.txt", "/event", "/final");
        host.Transport.Requests.Should().OnlyContain(item => item.Headers["Accept-Encoding"] == "gzip, br");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("identity", false)]
    [InlineData(null, true)]
    [InlineData("identity", true)]
    public async Task Fetch_ShouldPassABodyThroughUnchanged_WhenItHasNoEncodingOrIdentity(string? encoding, bool proxy)
    {
        var host = Create(proxy);
        byte[] page = Page(5);
        host.Transport.Responses.Enqueue(encoding is null ? FetchTestHost.Encoded(page) : FetchTestHost.Encoded(page, encoding));

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.Body.ToArray().Should().Equal(page);
        result.Usage.Bytes.Should().Be(page.Length);
    }

    [Theory]
    [InlineData("zstd", false, false)]
    [InlineData("deflate", false, false)]
    [InlineData("gzip, br", false, false)]
    [InlineData("corrupt-gzip", false, false)]
    [InlineData("corrupt-br", false, false)]
    [InlineData("truncated-gzip", false, false)]
    [InlineData("truncated-br", false, false)]
    [InlineData("gzip-without-its-last-byte", false, false)]
    [InlineData("br-without-its-last-byte", false, false)]
    [InlineData("truncated-gzip", true, true)]
    [InlineData("truncated-br", true, true)]
    [InlineData("zstd", true, true)]
    [InlineData("corrupt-gzip", true, true)]
    [InlineData("corrupt-br", false, true)]
    [InlineData("gzip, br", true, false)]
    public async Task Fetch_ShouldFailAsADecodeErrorWithoutPausingTheHost_WhenTheEncodingIsUnknownOrTheStreamIsCorrupt(
        string scenario, bool proxy, bool paced)
    {
        var host = Create(proxy);
        byte[] page = Page(20);
        host.Transport.Responses.Enqueue(scenario switch
        {
            "zstd" or "deflate" => FetchTestHost.Encoded(page, scenario),
            "gzip, br" => FetchTestHost.Encoded(Encode(Encode(page, "gzip"), "br"), "gzip", "br"),
            "corrupt-gzip" => FetchTestHost.Encoded([.. Encode(page, "gzip").Select((value, index) => index < 12 ? value : (byte)(value ^ 0x5a))], "gzip"),
            "corrupt-br" => FetchTestHost.Encoded(Enumerable.Range(0, 400).Select(index => (byte)(index * 37 + 11)).ToArray(), "br"),
            "truncated-gzip" => FetchTestHost.Encoded(Encode(page, "gzip")[..(Encode(page, "gzip").Length / 2)], "gzip"),
            "gzip-without-its-last-byte" => FetchTestHost.Encoded(Encode(page, "gzip")[..^1], "gzip"),
            "br-without-its-last-byte" => FetchTestHost.Encoded(Encode(page, "br")[..^1], "br"),
            _ => FetchTestHost.Encoded(Encode(page, "br")[..(Encode(page, "br").Length / 2)], "br"),
        });

        var result = await host.Fetcher.FetchAsync(Request(paced));

        result.Outcome.Should().Be(FetchOutcome.DecodeError);
        result.Message.Should().Contain("content encoding");
        result.Document!.StatusCode.Should().Be(502);
        result.RetryAfter.Should().BeNull();
        host.Hosts.State.PausedUntil.Should().BeNull("a decode failure is a failed fetch, not a refusal");
        host.Hosts.State.RetryAfterUntil.Should().BeNull();
        host.Hosts.State.PauseReason.Should().BeNull();
        host.Hosts.State.ConsecutiveFailures.Should().Be(0);
        host.Hosts.State.AdaptiveDelayMilliseconds.Should().Be(0);
        host.Hosts.Disposals.Should().Be(host.Hosts.Acquisitions);
    }

    [Theory]
    [InlineData("gzip", false, false)]
    [InlineData("br", false, false)]
    [InlineData("gzip", true, false)]
    [InlineData("br", false, true)]
    public async Task Fetch_ShouldFailAsTooLarge_WhenASmallEncodedBodyInflatesPastTheLimit(string encoding, bool proxy, bool paced)
    {
        var host = Create(proxy);
        byte[] bomb = Encode(new byte[6 * 1024 * 1024], encoding);
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(bomb, encoding));

        var result = await host.Fetcher.FetchAsync(Request(paced));

        bomb.Length.Should().BeLessThan(64 * 1024, "the wire length passes the Content-Length check");
        result.Outcome.Should().Be(FetchOutcome.ResponseTooLarge);
        result.Usage.Bytes.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(bomb.Length);
        host.Hosts.State.PauseReason.Should().Be(nameof(FetchOutcome.ResponseTooLarge));
    }

    [Theory]
    [InlineData(4096, FetchOutcome.Ok)]
    [InlineData(4097, FetchOutcome.ResponseTooLarge)]
    public async Task Fetch_ShouldApplyTheLimitToDecodedBytes_AtItsEdge(int decoded, FetchOutcome expected)
    {
        var host = Create(false);
        host.Options.MaximumResponseBytes = 4096;
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(Encode(new byte[decoded], "gzip"), "gzip"));

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task Fetch_ShouldStillStopEarly_WhenTheWireLengthAloneIsOverTheLimit()
    {
        var host = Create(false);
        host.Options.MaximumResponseBytes = 1024;
        byte[] wire = new byte[2048];
        new Random(7).NextBytes(wire);
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(wire, "gzip"));

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.ResponseTooLarge);
        result.Usage.Bytes.Should().Be(0, "nothing was read past the Content-Length check");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotModified)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Fetch_ShouldNotDecode_WhenTheReplyHasNoBodyAndNamesAnEncoding(HttpStatusCode status)
    {
        var host = Create(false);
        var reply = new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };
        reply.Content.Headers.ContentEncoding.Add("zstd");
        host.Transport.Responses.Enqueue(reply);

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task Fetch_ShouldMeterEveryWireByte_WhenBytesFollowTheEndOfABrotliStream()
    {
        var host = Create(false);
        byte[] page = Page(10);
        byte[] wire = [.. Encode(page, "br"), .. new byte[] { 1, 2, 3, 4, 5 }];
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded(wire, "br"));

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.Body.ToArray().Should().Equal(page);
        result.Usage.Bytes.Should().Be(wire.Length);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("br")]
    public async Task Fetch_ShouldReadAnEmptyEncodedBodyAsEmpty(string encoding)
    {
        var host = Create(false);
        host.Transport.Responses.Enqueue(FetchTestHost.Encoded([], encoding));

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.Body.Length.Should().Be(0);
        result.Usage.Bytes.Should().Be(0);
    }

    private static FetchTestHost Create(bool proxy)
    {
        var host = new FetchTestHost();
        if (proxy)
            host.TransportFactory = new ProxyRoutes(host.Transport);
        return host;
    }

    private static FetchRequest Request(bool paced = false) => new()
    {
        Uri = new Uri("https://example.com/event"),
        RobotsPolicy = RobotsPolicy.Ignore,
        Pace = paced ? new RequestPace { Gap = TimeSpan.Zero } : null,
    };

    /// <summary>Builds a page of repeating, compressible markup of about the given size in KiB.</summary>
    private static byte[] Page(int kibibytes)
    {
        var page = new StringBuilder("<html><body>");
        for (int index = 0; page.Length < kibibytes * 1024; index++)
            page.Append("<div class=\"ad\" data-id=\"").Append(index).Append("\">Apartment ").Append(index * 7 % 13).Append(" rooms, Tashkent</div>\n");
        return Encoding.UTF8.GetBytes(page.Append("</body></html>").ToString());
    }

    private static byte[] Encode(byte[] body, string encoding)
    {
        using var output = new MemoryStream();
        using (Stream encoder = encoding == "gzip" ? new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)
            : new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            encoder.Write(body);
        return output.ToArray();
    }

    /// <summary>A private route that is not the host's: what a fixed proxy exit looks like to the fetch handler.</summary>
    private sealed class ProxyRoutes(HttpMessageHandler handler) : IFetchTransportFactory
    {
        public ValueTask<IFetchTransportLease> AcquireAsync(Uri destination, CookieContainer? runCookies = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IFetchTransportLease>(new Route(new HttpClient(handler, disposeHandler: false), destination));
    }

    private sealed class Route(HttpClient client, Uri origin) : IFetchTransportLease
    {
        public HttpClient Client => client;
        public CookieContainer? Cookies => null;
        public bool IsPrivate => true;
        public string RouteKey => "olx-ip-server";
        public bool CachesRobotsByRoute => false;
        public bool Allows(Uri destination) => destination.IdnHost == origin.IdnHost;
        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
