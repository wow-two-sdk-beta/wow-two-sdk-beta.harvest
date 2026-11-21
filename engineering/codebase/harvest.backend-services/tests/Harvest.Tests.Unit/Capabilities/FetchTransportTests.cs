using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

public sealed class FetchTransportTests
{
    [Theory]
    [InlineData("denied", "open")]
    [InlineData("restricted", "open")]
    [InlineData("open", "denied")]
    [InlineData("open", "restricted")]
    public async Task Fetch_ShouldCheckPermissionsBeforeAcquiringRoute_WhenEitherHostGateRefuses(string permission, string policyPermission)
    {
        var (host, factory) = Create();
        host.Hosts.State.Permission = permission;
        host.Hosts.State.PolicyPermission = policyPermission;
        factory.AcquisitionFailure = new InvalidOperationException("A route must not be acquired.");

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.Restricted);
        factory.Acquisitions.Should().Be(0); host.Transport.Requests.Should().BeEmpty();
        host.Hosts.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fetch_ShouldCheckPauseBeforeAcquiringRoute_WhenHostBackoffIsActive(bool retryAfter)
    {
        var (host, factory) = Create();
        if (retryAfter) host.Hosts.State.RetryAfterUntil = host.Clock.Now.AddMinutes(5);
        else host.Hosts.State.PausedUntil = host.Clock.Now.AddMinutes(5);
        host.Hosts.State.PauseReason = nameof(FetchOutcome.RateLimited);
        factory.AcquisitionFailure = new InvalidOperationException("A route must not be acquired.");

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.RateLimited); result.RetryAfter.Should().Be(TimeSpan.FromMinutes(5));
        factory.Acquisitions.Should().Be(0); host.Transport.Requests.Should().BeEmpty();
        host.Hosts.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task Fetch_ShouldReturnUnavailableWithoutDirectFallback_WhenTheConfiguredRouteCannotBeAcquired()
    {
        var (host, factory) = Create();
        factory.AcquisitionFailure = new FetchTransportUnavailableException("provider-secret test-only");
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "A direct fallback must never read this."));

        var result = await host.Fetcher.FetchAsync(Request());

        result.Outcome.Should().Be(FetchOutcome.CapabilityUnavailable); result.Usage.Requests.Should().Be(0);
        result.Message.Should().NotContain("provider-secret");
        factory.Acquisitions.Should().Be(1); factory.Leases.Should().BeEmpty();
        host.Transport.Requests.Should().BeEmpty(); host.Hosts.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData("ok", FetchOutcome.Ok)]
    [InlineData("robots", FetchOutcome.RobotsDisallowed)]
    [InlineData("origin", FetchOutcome.InvalidRedirect)]
    [InlineData("network", FetchOutcome.NetworkError)]
    [InlineData("oversize", FetchOutcome.ResponseTooLarge)]
    [InlineData("cancel", FetchOutcome.Ok)]
    public async Task Fetch_ShouldDisposeRouteAndHostLeases_WhenTheOperationEnds(string scenario, FetchOutcome expected)
    {
        var (host, factory) = Create();
        using var cancellation = new CancellationTokenSource();
        var request = Request();
        switch (scenario)
        {
            case "robots":
                request = request with { RobotsPolicy = RobotsPolicy.Obey };
                host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nDisallow: /", type: "text/plain"));
                break;
            case "origin": factory.AllowDestination = _ => false; break;
            case "network":
                host.Transport.SendOverride = (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection failed"));
                break;
            case "cancel":
                host.Transport.SendOverride = (_, token) => { cancellation.Cancel(); return Task.FromCanceled<HttpResponseMessage>(token); };
                break;
            default:
                if (scenario == "oversize") host.Options.MaximumResponseBytes = 1;
                host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "content"));
                break;
        }

        if (scenario == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Fetcher.FetchAsync(request, cancellation.Token));
        else
            (await host.Fetcher.FetchAsync(request, cancellation.Token)).Outcome.Should().Be(expected);

        factory.Leases.Should().ContainSingle().Which.Disposed.Should().BeTrue();
        host.Hosts.Acquisitions.Should().Be(1); host.Hosts.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Fetch_ShouldKeepPublicValidatorsAndBodyIsolated_WhenTheSelectedRouteIsPrivate(bool privateRoute, bool sessionCookies)
    {
        var (host, factory) = Create();
        var first = FetchTestHost.Response(body: "public-body");
        first.Headers.ETag = new EntityTagHeaderValue("\"public-v1\"");
        var second = FetchTestHost.Response(body: "private-body");
        second.Headers.ETag = new EntityTagHeaderValue("\"private-v1\"");
        host.Transport.Responses.Enqueue(first);
        host.Transport.Responses.Enqueue(second);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(HttpStatusCode.NotModified, string.Empty));
        await host.Fetcher.FetchAsync(Request());
        factory.IsPrivate = privateRoute;
        factory.SessionCookies = sessionCookies ? new CookieContainer() : null;

        var privateResponse = await host.Fetcher.FetchAsync(Request());
        factory.IsPrivate = false; factory.SessionCookies = null;
        var publicResponse = await host.Fetcher.FetchAsync(Request());

        privateResponse.Outcome.Should().Be(FetchOutcome.Ok);
        host.Transport.Requests[1].Headers.Should().NotContainKey("If-None-Match");
        host.Transport.Requests[2].Headers["If-None-Match"].Should().Be("\"public-v1\"");
        publicResponse.NotModified.Should().BeTrue();
        Encoding.UTF8.GetString(publicResponse.Document!.Body.Span).Should().Be("public-body");
        factory.Leases.Should().OnlyContain(lease => lease.Disposed);
    }

    [Fact]
    public async Task Fetch_ShouldKeepPublicRobotsUnchanged_WhenAPrivateRouteReadsDifferentRules()
    {
        var (host, factory) = Create();
        const string publicRobots = "User-agent: *\nDisallow: /public-only";
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: publicRobots, type: "text/plain"));
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "public"));
        var request = Request() with { RobotsPolicy = RobotsPolicy.Obey };
        await host.Fetcher.FetchAsync(request);
        factory.IsPrivate = true; factory.CachesRobots = true;
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nDisallow: /private-only", type: "text/plain"));
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "private"));

        var privateResponse = await host.Fetcher.FetchAsync(request);
        factory.IsPrivate = false; factory.CachesRobots = false;
        var publicResponse = await host.Fetcher.FetchAsync(request with { Uri = new Uri("https://example.com/public-only") });

        privateResponse.Outcome.Should().Be(FetchOutcome.Ok); privateResponse.Usage.Requests.Should().Be(2, "the route's first fetch reads its own robots");
        host.Hosts.State.RobotsBody.Should().Be(publicRobots);
        publicResponse.Outcome.Should().Be(FetchOutcome.RobotsDisallowed); publicResponse.Usage.Requests.Should().Be(0);
        host.Transport.Requests.Should().HaveCount(4);
    }

    [Fact]
    public async Task Fetch_ShouldReadRobotsOncePerRoute_WhenAFixedProxyRouteFetchesTheSameHostAgain()
    {
        var (host, factory) = Create();
        factory.IsPrivate = true; factory.CachesRobots = true; factory.RouteKey = "olx-ip-server";
        var request = Request() with { RobotsPolicy = RobotsPolicy.Obey };
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nDisallow: /secret", type: "text/plain"));
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "first"));
        var first = await host.Fetcher.FetchAsync(request);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "second"));
        var second = await host.Fetcher.FetchAsync(request with { Uri = new Uri("https://example.com/other") });
        factory.RouteKey = "other-route";
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "third"));
        var third = await host.Fetcher.FetchAsync(request with { Uri = new Uri("https://example.com/secret") });
        factory.RouteKey = "olx-ip-server";
        var fourth = await host.Fetcher.FetchAsync(request with { Uri = new Uri("https://example.com/secret") });

        first.Usage.Requests.Should().Be(2); first.Outcome.Should().Be(FetchOutcome.Ok);
        second.Usage.Requests.Should().Be(1, "the same host on the same route reads robots from the cache"); second.Outcome.Should().Be(FetchOutcome.Ok);
        third.Usage.Requests.Should().Be(2, "another route key reads its own robots"); third.Outcome.Should().Be(FetchOutcome.Ok);
        fourth.Outcome.Should().Be(FetchOutcome.RobotsDisallowed, "the cached rules still bind"); fourth.Usage.Requests.Should().Be(0);
        host.Transport.Requests.Select(item => item.Uri.AbsolutePath).Should().Equal("/robots.txt", "/event", "/other", "/robots.txt", "/secret");
        host.Hosts.State.RobotsBody.Should().BeNull("a route's robots never reach the host's own state");
        factory.Leases.Should().OnlyContain(lease => lease.Disposed);
    }

    [Fact]
    public async Task Fetch_ShouldReadRouteRobotsAgain_WhenTheirLifetimeEnded()
    {
        var (host, factory) = Create();
        factory.IsPrivate = true; factory.CachesRobots = true;
        var request = Request() with { RobotsPolicy = RobotsPolicy.Obey };
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "one"));
        await host.Fetcher.FetchAsync(request);
        host.Clock.Now = host.Clock.Now.AddHours(23);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "two"));
        var cached = await host.Fetcher.FetchAsync(request);
        host.Clock.Now = host.Clock.Now.AddHours(2);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nDisallow: /event", type: "text/plain"));
        var expired = await host.Fetcher.FetchAsync(request);

        cached.Usage.Requests.Should().Be(1);
        expired.Usage.Requests.Should().Be(1, "the expired rules were read again, and they disallow the page");
        expired.Outcome.Should().Be(FetchOutcome.RobotsDisallowed);
        host.Transport.Requests.Count(item => item.Uri.AbsolutePath == "/robots.txt").Should().Be(2);
    }

    [Fact]
    public async Task Fetch_ShouldKeepARouteCrawlDelayBindingFromTheCache()
    {
        var (host, factory) = Create();
        factory.IsPrivate = true; factory.CachesRobots = true; factory.RouteKey = "olx-ip-server";
        var cached = host.RouteRobots.For("example.com", "olx-ip-server");
        cached.RobotsBody = "User-agent: *\nCrawl-delay: 5\nAllow: /"; cached.RobotsOrigin = "https://example.com";
        cached.RobotsStatus = 200; cached.RobotsCachedAt = host.Clock.Now;
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "page"));

        var result = await host.Fetcher.FetchAsync(Request() with { RobotsPolicy = RobotsPolicy.Obey, Pace = new RequestPace { Gap = TimeSpan.Zero } });

        result.Outcome.Should().Be(FetchOutcome.Ok); result.Usage.Requests.Should().Be(1);
        (host.Hosts.State.NextAllowedAt - host.Clock.Now).Should().Be(TimeSpan.FromSeconds(5));
        host.Transport.Requests.Select(item => item.Uri.AbsolutePath).Should().Equal("/event");
    }

    [Fact]
    public async Task Fetch_ShouldReadRobotsForEachFetch_WhenTheRouteCarriesASessionJar()
    {
        var (host, factory) = Create();
        factory.IsPrivate = true; factory.CachesRobots = true; factory.SessionCookies = new CookieContainer();
        var request = Request() with { RobotsPolicy = RobotsPolicy.Obey };
        for (int read = 0; read < 2; read++)
        {
            host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
            host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "page"));
            (await host.Fetcher.FetchAsync(request)).Usage.Requests.Should().Be(2);
        }

        host.Transport.Requests.Count(item => item.Uri.AbsolutePath == "/robots.txt").Should().Be(2);
    }

    [Fact]
    public async Task Fetch_ShouldReadRobotsForEachFetch_WhenTheRequestCarriesAuthorization()
    {
        var (host, factory) = Create();
        factory.IsPrivate = true; factory.CachesRobots = true;
        using var client = new HttpClient(new HarvestFetchHandler(factory, host.Hosts, host.Options, new GenericChallengeDetector(host.Clock),
            host.Clock, null, host.RouteRobots));
        for (int read = 0; read < 2; read++)
        {
            host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
            host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "page"));
            using var message = new HttpRequestMessage(HttpMethod.Get, "https://example.com/event");
            message.Headers.TryAddWithoutValidation("Authorization", "Bearer test-only");
            message.Options.Set(HarvestFetchMetadata.RequestKey, Request() with { RobotsPolicy = RobotsPolicy.Obey });
            using var reply = await client.SendAsync(message);
            reply.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        host.Transport.Requests.Count(item => item.Uri.AbsolutePath == "/robots.txt").Should().Be(2);
    }

    [Fact]
    public async Task Fetch_ShouldReadPrivateRobotsAgain_WhenAnotherSessionUsesTheSameHost()
    {
        var (host, factory) = Create();
        factory.IsPrivate = true;
        factory.SessionCookies = new CookieContainer();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nDisallow: /", type: "text/plain"));
        var request = Request() with { RobotsPolicy = RobotsPolicy.Obey };
        var first = await host.Fetcher.FetchAsync(request);
        factory.SessionCookies = new CookieContainer();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "second session"));

        var second = await host.Fetcher.FetchAsync(request);

        first.Outcome.Should().Be(FetchOutcome.RobotsDisallowed); second.Outcome.Should().Be(FetchOutcome.Ok);
        host.Transport.Requests.Count(item => item.Uri.AbsolutePath == "/robots.txt").Should().Be(2);
        host.Hosts.State.RobotsBody.Should().BeNull(); host.Hosts.State.RobotsOrigin.Should().BeNull();
        factory.Leases.Should().OnlyContain(lease => lease.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fetch_ShouldRejectDifferentOriginBeforeSending_WhenContentOrRobotsRedirectsToAnotherPort(bool robots)
    {
        var (host, factory) = Create();
        factory.IsPrivate = true;
        factory.SessionCookies = new CookieContainer();
        factory.SessionCookies.Add(new Uri("https://example.com"), new Cookie("session", "test-only"));
        var redirect = FetchTestHost.Response(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("https://example.com:8443/other");
        host.Transport.Responses.Enqueue(redirect);

        var result = await host.Fetcher.FetchAsync(Request() with { RobotsPolicy = robots ? RobotsPolicy.Obey : RobotsPolicy.Ignore });

        result.Outcome.Should().Be(FetchOutcome.InvalidRedirect);
        host.Transport.Requests.Should().ContainSingle();
        host.Transport.Requests[0].Headers.Should().ContainKey("Cookie");
        factory.Leases.Should().ContainSingle().Which.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Fetch_ShouldUseOneLeaseAcrossRobotsAndApprovedRedirects_WhenTheyStayInTheOrigin()
    {
        var (host, factory) = Create();
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "User-agent: *\nAllow: /", type: "text/plain"));
        var redirect = FetchTestHost.Response(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("/final", UriKind.Relative);
        host.Transport.Responses.Enqueue(redirect);
        host.Transport.Responses.Enqueue(FetchTestHost.Response(body: "final"));

        var result = await host.Fetcher.FetchAsync(Request() with { RobotsPolicy = RobotsPolicy.Obey });

        result.Outcome.Should().Be(FetchOutcome.Ok); result.Document!.FinalUrl.AbsolutePath.Should().Be("/final");
        factory.Acquisitions.Should().Be(1); factory.Leases.Should().ContainSingle().Which.Disposed.Should().BeTrue();
        host.Transport.Requests.Select(item => item.Uri.AbsolutePath).Should().Equal("/robots.txt", "/event", "/final");
    }

    [Fact]
    public async Task DirectRoute_ShouldRestrictOriginsAndAllowOnlyDefaultPortHttpsUpgrade()
    {
        var factory = new DirectFetchTransportFactory(new NamedClients(new FetchTestHost.ScriptedTransport()));
        await using var secure = await factory.AcquireAsync(new Uri("https://example.com/event"));
        secure.Allows(new Uri("https://example.com/final")).Should().BeTrue();
        foreach (var url in new[] { "https://example.com:8443/final", "https://other.example/final", "http://example.com/final", "https://user:password@example.com/final" })
            secure.Allows(new Uri(url)).Should().BeFalse();
        await using var plain = await factory.AcquireAsync(new Uri("http://example.com/event"));
        plain.Allows(new Uri("https://example.com/final")).Should().BeTrue();
        plain.Allows(new Uri("https://example.com:8443/final")).Should().BeFalse();
        await using var nonstandard = await factory.AcquireAsync(new Uri("http://example.com:8080/event"));
        nonstandard.Allows(new Uri("https://example.com/final")).Should().BeFalse();
    }

    [Fact]
    public void AddHarvestFetching_ShouldRetainAnExplicitTransportFactory_WhenTheHostRegistersOne()
    {
        var (_, factory) = Create();
        var services = new ServiceCollection().AddSingleton<IFetchTransportFactory>(factory);
        services.AddHarvestFetching();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IFetchTransportFactory>().Should().BeSameAs(factory);
    }

    private static (FetchTestHost Host, Routes Factory) Create()
    {
        var host = new FetchTestHost();
        var routes = new Routes(host.Transport);
        host.TransportFactory = routes;
        return (host, routes);
    }

    private static FetchRequest Request() => new() { Uri = new Uri("https://example.com/event"), RobotsPolicy = RobotsPolicy.Ignore };

    private sealed class Routes(HttpMessageHandler handler) : IFetchTransportFactory
    {
        internal int Acquisitions { get; private set; }
        internal List<Route> Leases { get; } = [];
        internal Exception? AcquisitionFailure { get; set; }
        internal bool IsPrivate { get; set; }
        internal bool CachesRobots { get; set; }
        internal string RouteKey { get; set; } = "test-route";
        internal CookieContainer? SessionCookies { get; set; }
        internal Func<Uri, bool>? AllowDestination { get; set; }

        public ValueTask<IFetchTransportLease> AcquireAsync(Uri destination, CookieContainer? runCookies = null, CancellationToken cancellationToken = default)
        {
            Acquisitions++;
            if (AcquisitionFailure is { } failure) throw failure;
            var route = new Route(new HttpClient(handler, disposeHandler: false), destination, IsPrivate, SessionCookies ?? runCookies, AllowDestination,
                CachesRobots, RouteKey);
            Leases.Add(route);
            return ValueTask.FromResult<IFetchTransportLease>(route);
        }
    }

    private sealed class Route(HttpClient client, Uri origin, bool isPrivate, CookieContainer? cookies, Func<Uri, bool>? allows, bool cachesRobots,
        string routeKey) : IFetchTransportLease
    {
        internal bool Disposed { get; private set; }
        public HttpClient Client => client;
        public CookieContainer? Cookies => cookies;
        public bool IsPrivate => isPrivate;
        public string RouteKey => routeKey;
        public bool CachesRobotsByRoute => cachesRobots;
        public bool Allows(Uri destination) => allows?.Invoke(destination)
            ?? destination.GetLeftPart(UriPartial.Authority).Equals(origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
        public ValueTask DisposeAsync() { Disposed = true; Client.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class NamedClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
