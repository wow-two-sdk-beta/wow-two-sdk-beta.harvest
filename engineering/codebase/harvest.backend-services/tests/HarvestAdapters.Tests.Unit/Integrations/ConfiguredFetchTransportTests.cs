using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;

namespace HarvestAdapters.Tests.Unit.Integrations;

public sealed class ConfiguredFetchTransportTests
{
    private static readonly Uri Source = new("https://source.example.test/event");

    [Theory]
    [InlineData("https://www.olx.uz")]
    [InlineData("https://olx.uz")]
    public async Task Acquire_ShouldSelectTheOlxProxyOnlyForItsOrigin(string origin)
    {
        var host = new IntegrationTestHost();
        host.Options.Enabled = true;
        host.Options.OlxProxy = new ProxyConnectionOptions { Server = "https://proxy.example.test:443" };
        var olx = new Uri(origin + "/apartments");
        await using IFetchTransportLease configured = await host.Routes.AcquireAsync(olx);
        await using IFetchTransportLease unrelated = await host.Routes.AcquireAsync(Source);
        configured.RouteKey.Should().Be("olx-ip-server");
        configured.IsPrivate.Should().BeTrue();
        unrelated.RouteKey.Should().Be("direct");
        host.Proxies.Calls.Should().HaveCount(1);
    }

    [Fact]
    public async Task Acquire_ShouldStayDirectWithoutProviderCalls_WhenDisabled()
    {
        var host = new IntegrationTestHost();
        await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Source);
        lease.RouteKey.Should().Be("direct"); lease.IsPrivate.Should().BeFalse();
        lease.Allows(Source).Should().BeTrue();
        lease.Allows(new Uri("https://source.example.test:444/event")).Should().BeFalse();
        lease.Allows(new Uri("http://source.example.test/event")).Should().BeFalse();
        host.Proxies.Calls.Should().BeEmpty(); host.Http.Calls.Should().BeEmpty();
        host.Http.Clients.Should().Equal("Harvest.Direct");
    }

    [Fact]
    public async Task Acquire_ShouldSwitchOlxBetweenNamedProfiles()
    {
        var host = new IntegrationTestHost();
        host.Options.Enabled = true;
        host.Options.ProxyProfilesJson = """
            {
              "geonode-uz": {"server":"https://geonode.example.test:443"},
              "decodo-uz": {"server":"socks5://decodo.example.test:1080"},
              "oxylabs-uz": {"server":"http://oxylabs.example.test:8080"},
              "bright-data-uz": {"server":"https://bright-data.example.test:443"},
              "pro-data-uz": {"server":"https://pro-data.example.test:443"}
            }
            """;
        host.Options.LoadProxyProfiles();
        var source = new Uri("https://www.olx.uz/apartments");
        foreach (string profile in new[] { "geonode-uz", "decodo-uz", "oxylabs-uz", "bright-data-uz", "pro-data-uz" })
        {
            host.Options.OlxProxyProfile = profile;
            host.Options.HasValidProxyConfiguration().Should().BeTrue();
            await using IFetchTransportLease lease = await host.Routes.AcquireAsync(source);
            lease.IsPrivate.Should().BeTrue();
            lease.Allows(source).Should().BeTrue();
            lease.Allows(Source).Should().BeFalse();
        }

        host.Proxies.Calls.Select(uri => uri.Host).Should().Equal(
            "geonode.example.test", "decodo.example.test", "oxylabs.example.test", "bright-data.example.test", "pro-data.example.test");
        host.Http.Clients.Should().BeEmpty();
        host.Options.OlxProxyProfile = "missing";
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(source));
        host.Http.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_ShouldPreserveTheCallerJar_WhenNoAccountIsConfigured()
    {
        var host = new IntegrationTestHost(); var jar = new CookieContainer();
        await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Source, jar);
        lease.Cookies.Should().BeSameAs(jar); lease.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public async Task Acquire_ShouldUseOneChosenProxyAndNeverRotate_WhenTheSourceRefuses()
    {
        var host = new IntegrationTestHost();
        host.AddRoute(FetchRouteMode.Proxy).Proxy = new ProxyConnectionOptions
        {
            Server = "socks5://proxy.example.test:1080", Username = "proxy-user", Password = "proxy-secret",
        };
        await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Source);
        using HttpResponseMessage response = await lease.Client.GetAsync(Source);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        lease.IsPrivate.Should().BeTrue(); host.Proxies.Calls.Should().HaveCount(1); host.Proxies.Requests.Should().Be(1);
        host.Http.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_ShouldFailClosedWithoutAFallback_WhenProxySettingsAreMissing()
    {
        var host = new IntegrationTestHost(); host.AddRoute(FetchRouteMode.Proxy);
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
        host.Http.Clients.Should().BeEmpty(); host.Proxies.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://source.example.test/path")]
    [InlineData("https://user:password@source.example.test")]
    [InlineData("source.example.test")]
    public async Task Acquire_ShouldRejectMalformedRouteOrigins_InsteadOfSilentlyGoingDirect(string origin)
    {
        var host = new IntegrationTestHost(); host.AddRoute().Origin = origin;
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
        host.Http.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_ShouldKeepAccountCookiesExclusiveAndOriginScoped_UntilTheLeaseReturns()
    {
        var host = new IntegrationTestHost(); host.AddRoute().SessionIds.AddRange(["account-a", "account-b"]);
        host.AddSession("account-a", "secret-a"); host.AddSession("account-b", "secret-b");
        IFetchTransportLease first = await host.Routes.AcquireAsync(Source);
        await using IFetchTransportLease second = await host.Routes.AcquireAsync(Source);
        first.Cookies!.GetCookieHeader(Source).Should().Be("session=secret-a");
        second.Cookies!.GetCookieHeader(Source).Should().Be("session=secret-b");
        first.Cookies.GetCookieHeader(new Uri("https://foreign.example.test/")).Should().BeEmpty();
        first.Allows(new Uri("https://source.example.test:444/event")).Should().BeFalse();
        first.IsPrivate.Should().BeTrue();
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
        await first.DisposeAsync();
        await first.DisposeAsync();
        await using IFetchTransportLease reused = await host.Routes.AcquireAsync(Source);
        reused.Cookies.Should().BeSameAs(first.Cookies);
        first.Allows(Source).Should().BeFalse();
    }

    [Fact]
    public async Task Acquire_ShouldRejectExpiredSessions_AndFenceAnAlreadyHeldSession()
    {
        var host = new IntegrationTestHost(); host.AddRoute().SessionIds.Add("account");
        host.AddSession("account", "private-cookie", TimeSpan.FromMinutes(1));
        await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Source);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        lease.Allows(Source).Should().BeFalse();
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
    }

    [Fact]
    public async Task Acquire_ShouldKeepAllConfiguredCookies_InALargerOperatorSession()
    {
        var host = new IntegrationTestHost(); host.AddRoute().SessionIds.Add("account"); host.AddSession("account", "cookie");
        for (int index = 0; index < 24; index++)
            host.Options.Sessions[0].Cookies.Add(new SessionCookieOptions { Name = $"state{index}", Value = $"value{index}" });
        await using IFetchTransportLease lease = await host.Routes.AcquireAsync(Source);
        lease.Cookies!.GetCookies(Source).Count.Should().Be(25);
    }

    [Fact]
    public async Task Acquire_ShouldRejectSessionCookiesOnHttp_WithoutSendingThem()
    {
        var host = new IntegrationTestHost();
        host.AddRoute().Origin = "http://source.example.test";
        host.Options.Routes[0].SessionIds.Add("account"); host.AddSession("account", "private-cookie");
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(new Uri("http://source.example.test/")));
        host.Http.Clients.Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_ShouldReleaseAnIpLeaseExactlyOnce_WithoutForwardingTheBrokerKeyToTheSource()
    {
        var host = new IntegrationTestHost();
        host.AddRoute(FetchRouteMode.IpLease).IpLease = Broker();
        host.Http.Replies.Enqueue("""{"leaseId":"lease-1","proxyUri":"https://proxy.example.test:8443","username":"route-user","password":"route-password","expiresAtUtc":"2026-10-27T10:00:00Z"}""");
        IFetchTransportLease lease = await host.Routes.AcquireAsync(Source);
        host.Proxies.Calls.Single().AbsoluteUri.Should().Be("https://proxy.example.test:8443/");
        lease.IsPrivate.Should().BeTrue();
        using HttpResponseMessage response = await lease.Client.GetAsync(Source);
        host.Proxies.Headers.Single().Should().NotContainKey("X-Api-Key").And.NotContainKey("Cookie");
        await lease.DisposeAsync(); await lease.DisposeAsync();
        host.Http.Calls.Should().HaveCount(2);
        host.Http.Calls[0].Uri.AbsoluteUri.Should().Be("https://broker.example.test/v1/leases/acquire");
        host.Http.Calls[1].Uri.AbsoluteUri.Should().Be("https://broker.example.test/v1/leases/release");
        host.Http.Calls.Should().OnlyContain(call => !call.Headers.ContainsKey("Cookie"));
        host.Http.Calls[0].Headers["X-Api-Key"].Should().Be("broker-test-key");
        using JsonDocument body = JsonDocument.Parse(host.Http.Calls[0].Body!);
        body.RootElement.GetProperty("origin").GetString().Should().Be("https://source.example.test");
        body.RootElement.GetProperty("routeKey").GetString().Should().Be("source");
        host.Http.Calls[0].Body.Should().NotContain("broker-test-key").And.NotContain("session");
    }

    [Theory]
    [InlineData("https://foreign-proxy.example.test:8443", "2026-10-27T10:00:00Z")]
    [InlineData("https://proxy.example.test:8443", "2026-10-27T08:00:00Z")]
    [InlineData("http://127.0.0.1:8080", "2026-10-27T10:00:00Z")]
    public async Task Acquire_ShouldRejectUnapprovedOrExpiredIpLeases_WithoutRetrying(string proxy, string expiry)
    {
        var host = new IntegrationTestHost(); host.AddRoute(FetchRouteMode.IpLease).IpLease = Broker();
        host.Http.Replies.Enqueue(JsonSerializer.Serialize(new { leaseId = "lease-1", proxyUri = proxy, expiresAtUtc = expiry }));
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
        host.Http.Calls.Should().HaveCount(1); host.Proxies.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Acquire_ShouldReleaseItsAccountSlot_WhenTheIpBrokerFails()
    {
        var host = new IntegrationTestHost(); FetchRouteOptions route = host.AddRoute(FetchRouteMode.IpLease);
        route.SessionIds.Add("account"); route.IpLease = Broker(); host.AddSession("account", "cookie");
        host.Http.Status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
        route.Mode = FetchRouteMode.Direct;
        await using IFetchTransportLease retry = await host.Routes.AcquireAsync(Source);
        retry.Cookies!.GetCookieHeader(Source).Should().Be("session=cookie");
    }

    [Theory]
    [InlineData("host")]
    [InlineData("Cookie")]
    [InlineData("Proxy-Authorization")]
    public async Task Acquire_ShouldRejectMisplacedBrokerSecrets_BeforeAnyNetworkCall(string header)
    {
        var host = new IntegrationTestHost();
        IpLeaseBrokerOptions broker = Broker(); broker.ApiKeyHeader = header;
        host.AddRoute(FetchRouteMode.IpLease).IpLease = broker;
        await Assert.ThrowsAsync<FetchTransportUnavailableException>(async () => await host.Routes.AcquireAsync(Source));
        host.Http.Calls.Should().BeEmpty(); host.Proxies.Calls.Should().BeEmpty();
    }

    private static IpLeaseBrokerOptions Broker()
    {
        return new IpLeaseBrokerOptions
        {
            BaseUrl = "https://broker.example.test/v1/", ApiKey = "broker-test-key", AllowedProxyHosts = ["proxy.example.test"],
        };
    }
}
