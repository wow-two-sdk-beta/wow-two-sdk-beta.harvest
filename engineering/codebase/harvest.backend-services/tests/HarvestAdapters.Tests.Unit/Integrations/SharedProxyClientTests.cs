using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace HarvestAdapters.Tests.Unit.Integrations;

/// <summary>A fixed proxy route keeps one guarded client, and so one connection pool, for the process; every per-fetch check still
/// runs, and a route that leases its IP from a broker keeps a client for each lease.</summary>
public sealed class SharedProxyClientTests
{
    private static readonly Uri Www = new("https://www.olx.uz/apartments");
    private static readonly Uri Bare = new("https://olx.uz/apartments");
    private static readonly Uri Source = new("https://source.example.test/event");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Acquire_ShouldReuseOneClientAcrossSequentialFetches_WhenTheRouteIsAFixedProxy(bool profile)
    {
        var host = FixedOlx(profile);
        var clients = new List<HttpClient>();

        for (int fetch = 0; fetch < 3; fetch++)
        {
            await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Www);
            clients.Add(lease.Client);
            using HttpResponseMessage response = await lease.Client.GetAsync(Www);
            lease.RouteKey.Should().Be("olx-ip-server");
            lease.IsPrivate.Should().BeTrue();
            lease.Allows(Www).Should().BeTrue();
            lease.Allows(Source).Should().BeFalse();
        }

        host.Proxies.Calls.Should().HaveCount(1, "the handler is built once");
        host.Proxies.Requests.Should().Be(3);
        host.Proxies.Disposed.Should().Be(0, "a fetch's lease leaves the shared client for the next fetch");
        clients.Should().OnlyContain(client => ReferenceEquals(client, clients[0]));
        host.Http.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_ShouldShareOneClientAmongParallelFetches()
    {
        var host = FixedOlx(profile: false);

        IFetchTransportLease[] leases = await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ => await host.Routes.AcquireAsync(Www)));

        host.Proxies.Calls.Should().HaveCount(1);
        leases.Select(lease => lease.Client).Distinct().Should().ContainSingle();
        foreach (IFetchTransportLease lease in leases)
            await lease.DisposeAsync();
        host.Proxies.Disposed.Should().Be(0);
    }

    [Fact]
    public async Task Acquire_ShouldKeepOneClientForEachSourceOrigin()
    {
        var host = FixedOlx(profile: false);

        foreach (Uri source in new[] { Www, Bare, Www, Bare })
        {
            await using IFetchTransportLease lease = await host.Routes.AcquireAsync(source);
            lease.Allows(source).Should().BeTrue();
        }

        host.Proxies.Calls.Should().HaveCount(2, "www.olx.uz and olx.uz are different origins");
    }

    [Theory]
    [InlineData("server")]
    [InlineData("username")]
    [InlineData("password")]
    public async Task Acquire_ShouldBuildANewClient_WhenTheProxySettingsChange(string changed)
    {
        var host = FixedOlx(profile: false);
        await using IFetchTransportLease first = await host.Routes.AcquireAsync(Www);
        ProxyConnectionOptions proxy = host.Options.OlxProxy!;
        switch (changed)
        {
            case "server": proxy.Server = "https://other-proxy.example.test:443"; break;
            case "username": proxy.Username = "other-user"; break;
            default: proxy.Password = "other-secret"; break;
        }

        await using IFetchTransportLease second = await host.Routes.AcquireAsync(Www);
        await using IFetchTransportLease again = await host.Routes.AcquireAsync(Www);

        host.Proxies.Calls.Should().HaveCount(2);
        second.Client.Should().NotBeSameAs(first.Client);
        again.Client.Should().BeSameAs(second.Client);
        using HttpResponseMessage inFlight = await first.Client.GetAsync(Www);
        host.Proxies.Disposed.Should().Be(0, "a fetch still reading through the old client is not cut off");
    }

    [Fact]
    public async Task Acquire_ShouldBuildANewClient_WhenTheProfileBehindTheRouteChanges()
    {
        var host = FixedOlx(profile: true);
        await using IFetchTransportLease first = await host.Routes.AcquireAsync(Www);
        host.Options.ProxyProfilesJson = """{"fixed-uz": {"server":"https://proxy.example.test:443","username":"u","password":"rotated"}}""";
        host.Options.LoadProxyProfiles();

        await using IFetchTransportLease second = await host.Routes.AcquireAsync(Www);

        host.Proxies.Calls.Should().HaveCount(2);
        second.Client.Should().NotBeSameAs(first.Client);
    }

    [Fact]
    public async Task Acquire_ShouldStopUsingTheSharedClient_WhenTheIntegrationIsDisabled()
    {
        var host = FixedOlx(profile: false);
        await using (IFetchTransportLease routed = await host.Routes.AcquireAsync(Www))
            using (await routed.Client.GetAsync(Www)) { }

        host.Options.Enabled = false;
        await using (IFetchTransportLease direct = await host.Routes.AcquireAsync(Www))
        {
            direct.RouteKey.Should().Be("direct");
            direct.IsPrivate.Should().BeFalse();
            direct.CachesRobotsByRoute.Should().BeFalse();
            using HttpResponseMessage response = await direct.Client.GetAsync(Www);
        }

        host.Proxies.Requests.Should().Be(1, "the disabled integration sent nothing through the proxy");
        host.Http.Clients.Should().Equal("Harvest.Direct");
        host.Options.Enabled = true;
        await using IFetchTransportLease back = await host.Routes.AcquireAsync(Www);
        back.RouteKey.Should().Be("olx-ip-server");
        host.Proxies.Calls.Should().HaveCount(1, "the client built before is used again");
    }

    [Fact]
    public async Task Acquire_ShouldFailClosedAndCacheNothing_WhenTheProxyEndpointIsInvalid()
    {
        var host = FixedOlx(profile: false);
        host.Options.OlxProxy!.Server = "https://proxy.example.test:443/with/a/path";
        var real = new ConfiguredFetchTransportFactory(host.Http, Options.Create(host.Options), host.Sessions,
            new GuardedProxyClientFactory(new LoopbackResolver()), new OutboundProxyBroker(host.Http, host.Clock), host.Clock);

        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await real.AcquireAsync(Www));
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await real.AcquireAsync(Www));
        host.Options.OlxProxy.Server = "https://proxy.example.test:443";
        await using IFetchTransportLease fixedLease = await real.AcquireAsync(Www);

        fixedLease.RouteKey.Should().Be("olx-ip-server");
        host.Http.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_ShouldDisposeEverySharedClient_AtShutdown()
    {
        var host = FixedOlx(profile: false);
        await using (await host.Routes.AcquireAsync(Www)) { }
        await using (await host.Routes.AcquireAsync(Bare)) { }
        host.Options.OlxProxy!.Password = "rotated";
        await using (await host.Routes.AcquireAsync(Www)) { }
        host.Proxies.Calls.Should().HaveCount(3);
        host.Proxies.Disposed.Should().Be(0);

        await host.Routes.DisposeAsync();
        await host.Routes.DisposeAsync();

        host.Proxies.Disposed.Should().Be(3, "the two current clients and the one a settings change retired");
    }

    [Fact]
    public async Task Acquire_ShouldKeepAClientForEachLease_WhenTheRouteLeasesItsIpFromABroker()
    {
        var host = new IntegrationTestHost();
        host.AddRoute(FetchRouteMode.IpLease).IpLease = new IpLeaseBrokerOptions
        {
            BaseUrl = "https://broker.example.test/v1/", ApiKey = "broker-test-key", AllowedProxyHosts = ["proxy.example.test"],
        };
        for (int fetch = 0; fetch < 2; fetch++)
        {
            host.Http.Replies.Enqueue($$"""{"leaseId":"lease-{{fetch}}","proxyUri":"https://proxy.example.test:8443","expiresAtUtc":"2026-10-27T10:00:00Z"}""");
            await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Source);
            lease.CachesRobotsByRoute.Should().BeTrue("a broker's exit does not change what a site's robots say");
        }

        host.Proxies.Calls.Should().HaveCount(2);
        host.Proxies.Disposed.Should().Be(2, "each lease disposes its own client");
    }

    [Fact]
    public async Task Acquire_ShouldNotShareRobots_WhenASessionOrACallerJarIsInvolved()
    {
        var host = new IntegrationTestHost();
        FetchRouteOptions route = host.AddRoute(FetchRouteMode.Proxy);
        route.Proxy = new ProxyConnectionOptions { Server = "https://proxy.example.test:443" };
        await using (IFetchTransportLease plain = await host.Routes.AcquireAsync(Source))
            plain.CachesRobotsByRoute.Should().BeTrue();
        await using (IFetchTransportLease jar = await host.Routes.AcquireAsync(Source, new CookieContainer()))
            jar.CachesRobotsByRoute.Should().BeFalse();
        route.SessionIds.Add("account");
        host.AddSession("account", "cookie");

        await using IFetchTransportLease session = await host.Routes.AcquireAsync(Source);

        session.CachesRobotsByRoute.Should().BeFalse();
        host.Proxies.Calls.Should().HaveCount(1, "a session does not change the proxy connection");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedClient_ShouldStayUsable_AfterTheFetchLeasesThatBorrowedItAreDisposed(bool profile)
    {
        var host = new IntegrationTestHost { Options = { Enabled = true } };
        if (profile)
        {
            host.Options.ProxyProfilesJson = """{"fixed-uz": {"server":"https://proxy.example.test:443"}}""";
            host.Options.LoadProxyProfiles();
            host.Options.OlxProxyProfile = "fixed-uz";
        }
        else
        {
            host.Options.OlxProxy = new ProxyConnectionOptions { Server = "https://proxy.example.test:443" };
        }
        // The core handler is bound to the lease it is built from; the resolver sends every destination to a non-public address, so
        // a request that passes the lease check stops at the address check instead.
        var routes = new ConfiguredFetchTransportFactory(host.Http, Options.Create(host.Options), host.Sessions,
            new GuardedProxyClientFactory(new LoopbackResolver()), new OutboundProxyBroker(host.Http, host.Clock), host.Clock);
        var clients = new List<HttpClient>();

        for (int fetch = 0; fetch < 3; fetch++)
        {
            await using IFetchTransportLease lease = await routes.AcquireAsync(Www);
            clients.Add(lease.Client);
            await Assert.ThrowsAsync<OutboundAddressBlockedException>(() => lease.Client.GetAsync(Www));
        }

        clients.Should().OnlyContain(client => ReferenceEquals(client, clients[0]));
        await routes.DisposeAsync();
    }

    private static IntegrationTestHost FixedOlx(bool profile)
    {
        var host = new IntegrationTestHost { Options = { Enabled = true } };
        if (profile)
        {
            host.Options.ProxyProfilesJson = """{"fixed-uz": {"server":"https://proxy.example.test:443","username":"u","password":"p"}}""";
            host.Options.LoadProxyProfiles();
            host.Options.OlxProxyProfile = "fixed-uz";
        }
        else
        {
            host.Options.OlxProxy = new ProxyConnectionOptions { Server = "https://proxy.example.test:443", Username = "u", Password = "p" };
        }

        return host;
    }

    private sealed class LoopbackResolver : IOutboundAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default) => Task.FromResult(new[] { IPAddress.Loopback });
    }
}
