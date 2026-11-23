using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

public sealed class PageFetcherTests
{
    [Fact]
    public async Task Fetch_ShouldKeepTheGuardAndUseANoDrainClientOnlyForOptedInRequests()
    {
        var services = new ServiceCollection();
        services.AddHarvestFetching();
        using var provider = services.BuildServiceProvider();
        var transports = provider.GetRequiredService<IFetchTransportFactory>();
        var address = new Uri("https://example.com/catalog");
        await using var full = await transports.AcquireAsync(address);
        await using var prefix = await transports.AcquireAsync(address, runCookies: null, allowPrefix: true, cancellationToken: default);
        full.SupportsEarlyBodyStop.Should().BeFalse();
        prefix.SupportsEarlyBodyStop.Should().BeTrue();

        var handlers = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        HttpMessageHandler prefixHandler = handlers.CreateHandler(HarvestFetchClientFactory.PrefixTransportName);
        HttpMessageHandler fullHandler = handlers.CreateHandler(HarvestFetchClientFactory.TransportName);
        SocketsTransport(prefixHandler).MaxResponseDrainSize.Should().Be(0);
        SocketsTransport(fullHandler).MaxResponseDrainSize.Should().BeGreaterThan(0);

        // The guarded client refuses a disallowed destination without opening a socket.
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/private");
        await Assert.ThrowsAnyAsync<HttpRequestException>(async () => await prefix.Client.SendAsync(request));

        static SocketsHttpHandler SocketsTransport(HttpMessageHandler handler)
        {
            while (handler is DelegatingHandler { InnerHandler: { } inner })
                handler = inner;
            return Assert.IsType<SocketsHttpHandler>(handler);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fetch_ShouldUseTheFullResponseOnFallback_AndNeverCacheAnEarlyPrefix(bool paced)
    {
        var host = new FetchTestHost { SupportsEarlyBodyStop = true };
        string tail = new string('b', 90000);
        var catalog = FetchTestHost.Response(body: new string('a', 70000) + "catalog-end" + tail);
        catalog.Headers.ETag = new EntityTagHeaderValue("\"prefix-v1\"");
        host.Transport.Responses.Enqueue(catalog);
        var fallback = FetchTestHost.Response(body: tail);
        fallback.Headers.ETag = new EntityTagHeaderValue("\"whole-v2\"");
        host.Transport.Responses.Enqueue(fallback);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.NotModified, string.Empty));
        var request = new FetchRequest
        {
            Uri = new Uri("https://example.com/catalog"),
            RobotsPolicy = RobotsPolicy.Ignore,
            HtmlPrefixPolicy = new MarkerPrefixPolicy(),
            Pace = paced ? new RequestPace { Gap = TimeSpan.Zero } : null,
        };

        var prefix = await host.Fetcher.FetchAsync(request);
        prefix.Outcome.Should().Be(FetchOutcome.Ok);
        prefix.Document!.IsBodyPrefix.Should().BeTrue();
        prefix.Usage.Bytes.Should().BeLessThan(160011);
        prefix.Document.Headers.Should().NotContain(header => header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
            && header.Value != prefix.Document.Body.Length.ToString());

        var whole = await host.Fetcher.FetchAsync(request);
        whole.Outcome.Should().Be(FetchOutcome.Ok);
        whole.Document!.IsBodyPrefix.Should().BeFalse();
        whole.Usage.Bytes.Should().Be(tail.Length);
        host.Transport.Requests[1].Headers.Should().NotContainKey("If-None-Match");

        var unchanged = await host.Fetcher.FetchAsync(request);
        unchanged.NotModified.Should().BeTrue();
        unchanged.Document!.IsBodyPrefix.Should().BeFalse();
        unchanged.Document.Body.Length.Should().Be(tail.Length);
        host.Transport.Requests[2].Headers["If-None-Match"].Should().Be("\"whole-v2\"");
    }

    private sealed class MarkerPrefixPolicy : IFetchHtmlPrefixPolicy
    {
        public bool AllowsPrefix(Uri destination) => destination.AbsolutePath == "/catalog";
        public bool ShouldStopReading(Uri destination, ReadOnlySpan<byte> prefix, int previousLength) =>
            prefix.IndexOf("catalog-end"u8) >= 0;
    }

    [Fact]
    public async Task Fetch_ShouldApplyTheSourceRouteRestrictionBeforeTransport()
    {
        var host = new FetchTestHost();

        FetchResponse result = await host.Fetcher.FetchAsync(new FetchRequest
        {
            Uri = new Uri("https://example.com/account"), RobotsPolicy = RobotsPolicy.Ignore, DestinationPolicy = new CatalogPolicy(),
        });

        result.Outcome.Should().Be(FetchOutcome.NotApproved);
        host.Transport.Requests.Should().BeEmpty();
        result.Usage.Requests.Should().Be(0);
    }

    [Fact]
    public async Task Fetch_ShouldRejectARedirectOutsideTheSourceRoutesBeforeFollowingIt()
    {
        var host = new FetchTestHost();
        var redirect = FetchTestHost.Response(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("https://example.com/account");
        host.Transport.Responses.Enqueue(redirect);

        FetchResponse result = await host.Fetcher.FetchAsync(new FetchRequest
        {
            Uri = new Uri("https://example.com/catalog/ad"), RobotsPolicy = RobotsPolicy.Ignore, DestinationPolicy = new CatalogPolicy(),
        });

        result.Outcome.Should().Be(FetchOutcome.InvalidRedirect);
        host.Transport.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/catalog/ad");
    }

    private sealed class CatalogPolicy : IFetchDestinationPolicy
    {
        public bool Allows(Uri destination) => destination.AbsolutePath.StartsWith("/catalog/", StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAsync_ShouldNotRequestADisallowedPath_WhenRobotsRefusesIt()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nDisallow: /private", type: "text/plain"));

        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/private/a") });

        result.Outcome.Should().Be(FetchOutcome.RobotsDisallowed);
        host.Transport.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/robots.txt");
        host.Transport.Requests[0].Headers["User-Agent"].Should().Be("Harvest/0.1");
    }

    [Fact]
    public async Task FetchAsync_ShouldTreatRobots403AsDisallowAll()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.Forbidden));
        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a") });
        result.Outcome.Should().Be(FetchOutcome.RobotsDisallowed);
        host.Transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchAsync_ShouldReuseRobotsCache_AndPreserveConditional304()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.NotFound, string.Empty, "text/plain"));
        var first = FetchTestHost.Response(body: "event");
        first.Headers.ETag = new EntityTagHeaderValue("\"event-v1\"");
        first.Content.Headers.LastModified = host.Clock.Now.AddDays(-1);
        host.Transport.Responses.Enqueue(first);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.NotModified, string.Empty));
        var request = new FetchRequest { Uri = new Uri("https://example.com/event") };

        var original = await host.Fetcher.FetchAsync(request);
        original.Outcome.Should().Be(FetchOutcome.Ok);
        original.Usage.Requests.Should().Be(2, "robots and content are separate transport attempts");
        original.Usage.Bytes.Should().Be(5);
        var again = await host.Fetcher.FetchAsync(request);

        again.Outcome.Should().Be(FetchOutcome.Ok);
        again.NotModified.Should().BeTrue();
        again.Usage.Requests.Should().Be(1);
        again.Usage.Bytes.Should().Be(0, "replaying a cached body reads no network bytes");
        again.Document!.StatusCode.Should().Be(304);
        System.Text.Encoding.UTF8.GetString(again.Document.Body.Span).Should().Be("event", "304 replays content even if the first worker died before ingesting it");
        host.Transport.Requests.Should().HaveCount(3);
        host.Transport.Requests[2].Headers["If-None-Match"].Should().Be("\"event-v1\"");
        host.Transport.Requests[2].Headers["If-Modified-Since"].Should().Be(host.Clock.Now.AddDays(-1).ToString("r"));
    }

    [Fact]
    public async Task FetchAsync_ShouldPauseWithoutRetrying_WhenTheHostChallengesIt()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.Forbidden, "<html>Verify you are human: hcaptcha.com</html>"));
        var request = new FetchRequest { Uri = new Uri("https://example.com/event"), RobotsPolicy = RobotsPolicy.Ignore };

        var first = await host.Fetcher.FetchAsync(request);
        var second = await host.Fetcher.FetchAsync(request);

        first.Outcome.Should().Be(FetchOutcome.Challenge);
        second.Outcome.Should().Be(FetchOutcome.Challenge);
        second.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));
        second.Usage.Requests.Should().Be(0);
        host.Transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchAsync_ShouldWaitAtLeastRetryAfter_WhenThrottled()
    {
        var host = new FetchTestHost();
        var response = FetchTestHost.Response(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(30));
        host.Transport.Responses.Enqueue(response);

        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore });

        result.Outcome.Should().Be(FetchOutcome.RateLimited);
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(30));
        host.Hosts.State.RetryAfterUntil.Should().Be(host.Clock.Now.AddMinutes(30));
        host.Hosts.State.PausedUntil.Should().Be(host.Clock.Now.AddMinutes(30));
    }

    [Fact]
    public async Task FetchAsync_ShouldKeepGoneDistinctFromAnUnavailableHost()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.Gone));
        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore });
        result.Outcome.Should().Be(FetchOutcome.Gone);
        result.Document!.StatusCode.Should().Be(410);
    }

    [Fact]
    public async Task FetchAsync_ShouldCountEveryAllowedRedirectHop()
    {
        var host = new FetchTestHost();
        var redirect = FetchTestHost.Response(HttpStatusCode.Redirect, "redirect");
        redirect.Headers.Location = new Uri("/final", UriKind.Relative);
        host.Transport.Responses.Enqueue(redirect);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "event"));
        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/start"), RobotsPolicy = RobotsPolicy.Ignore });
        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document!.FinalUrl.AbsolutePath.Should().Be("/final");
        result.Usage.Requests.Should().Be(2);
        result.Usage.Bytes.Should().Be(13);
    }

    [Fact]
    public async Task FetchAsync_ShouldKeepCookieRunsOutOfTheSharedConditionalCache()
    {
        var host = new FetchTestHost();
        var first = FetchTestHost.Response();
        first.Headers.ETag = new EntityTagHeaderValue("\"public\"");
        host.Transport.Responses.Enqueue(first);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "anonymous session"));
        var request = new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore };
        await host.Fetcher.FetchAsync(request);
        await host.Fetcher.FetchAsync(request with { Cookies = new CookieContainer() });
        host.Transport.Requests[1].Headers.Should().NotContainKey("If-None-Match");
    }

    [Fact]
    public async Task FetchAsync_ShouldForgetOlderValidators_WhenTheResponseBecomesPrivate()
    {
        var host = new FetchTestHost();
        var first = FetchTestHost.Response();
        first.Headers.ETag = new EntityTagHeaderValue("\"public\"");
        var second = FetchTestHost.Response(body: "private content");
        second.Headers.CacheControl = new CacheControlHeaderValue { Private = true };
        host.Transport.Responses.Enqueue(first);
        host.Transport.Responses.Enqueue(second);
        host.Transport.Responses.Enqueue(FetchTestHost.Response());
        var request = new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore };
        await host.Fetcher.FetchAsync(request);
        await host.Fetcher.FetchAsync(request);
        await host.Fetcher.FetchAsync(request);
        host.Transport.Requests[1].Headers.Should().ContainKey("If-None-Match");
        host.Transport.Requests[2].Headers.Should().NotContainKey("If-None-Match");
    }

    [Fact]
    public async Task FetchAsync_ShouldRefuseContent_WhenRobotsIsTemporarilyUnavailable()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.ServiceUnavailable, "busy", "text/plain"));
        var response = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a") });
        response.Outcome.Should().Be(FetchOutcome.HttpError);
        response.Usage.Requests.Should().Be(1);
        host.Transport.Requests.Should().ContainSingle().Which.Uri.AbsolutePath.Should().Be("/robots.txt");
    }

    [Theory]
    [InlineData("https://other.example/a")]
    [InlineData("http://example.com/a")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@example.com/a")]
    public async Task FetchAsync_ShouldRejectUnsafeRedirects_BeforeConnecting(string location)
    {
        var host = new FetchTestHost();
        var response = FetchTestHost.Response(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(location);
        host.Transport.Responses.Enqueue(response);

        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/event"), RobotsPolicy = RobotsPolicy.Ignore });

        result.Outcome.Should().Be(FetchOutcome.InvalidRedirect);
        host.Transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task FetchAsync_ShouldEnforceAByteLimit_WithoutReturningPartialContent()
    {
        var host = new FetchTestHost();
        host.Options.MaximumResponseBytes = 4;
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "too much content"));
        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore });
        result.Outcome.Should().Be(FetchOutcome.ResponseTooLarge);
        result.Document!.Body.ToArray().Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldPersistTheRequestedMinimumGap()
    {
        var host = new FetchTestHost();
        host.Transport.Responses.Enqueue(FetchTestHost.Response());
        await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore, MinDelay = TimeSpan.FromSeconds(9) });
        host.Hosts.State.NextAllowedAt.Should().Be(host.Clock.Now.AddSeconds(9));
    }

    [Theory]
    [InlineData("restricted")]
    [InlineData("denied")]
    public async Task FetchAsync_ShouldRespectAnOperatorHostPolicy_BeforeRobotsOrContent(string permission)
    {
        var host = new FetchTestHost();
        host.Hosts.State.PolicyPermission = permission;
        var result = await host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a") });
        result.Outcome.Should().Be(FetchOutcome.Restricted);
        host.Transport.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldCancelWhileWaitingForAHost_WithoutSending()
    {
        var host = new FetchTestHost();
        host.Hosts.State.NextAllowedAt = host.Clock.Now.AddHours(1);
        using var cancellation = new CancellationTokenSource();
        var fetch = host.Fetcher.FetchAsync(new FetchRequest { Uri = new Uri("https://example.com/a"), RobotsPolicy = RobotsPolicy.Ignore }, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        host.Transport.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Mozilla/5.0")]
    [InlineData("Mozilla/5.0 Harvest/0.1")]
    public void AddHarvestFetching_ShouldRejectAnAnonymousUserAgent_AndAnImpoliteGap(string userAgent)
    {
        var services = new ServiceCollection();
        services.AddHarvestFetching(options => { options.UserAgent = userAgent; options.MinimumDelay = TimeSpan.Zero; });
        using var provider = services.BuildServiceProvider();
        var read = () => provider.GetRequiredService<IOptions<HarvestFetchOptions>>().Value;
        read.Should().Throw<OptionsValidationException>().Which.Failures.Should().HaveCount(2);
    }
}
