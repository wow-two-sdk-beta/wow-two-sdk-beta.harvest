using System.Net;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace HarvestAdapters.Tests.Unit.Integrations;

public sealed class IntegrationFetchLeaseTests
{
    [Fact]
    public async Task Dispose_ShouldReturnAccountAndProxy_EvenWhenTheClientThrows()
    {
        int accounts = 0;
        var cookies = new CookieContainer();
        var account = new AccountSessionLease(cookies, DateTimeOffset.MaxValue, () => accounts++);
        var host = new IntegrationTestHost();
        host.Http.Replies.Enqueue("""{"leaseId":"lease-1","proxyUri":"https://proxy.example.test:443","expiresAtUtc":"2026-10-27T10:00:00Z"}""");
        var destination = new Uri("https://source.example.test");
        var broker = new OutboundProxyBroker(host.Http, host.Clock);
        var profile = new OutboundProxyProfileOptions
        {
            Broker = new OutboundProxyBrokerOptions
            {
                BaseUrl = "https://broker.example.test",
                ApiKey = "test-key",
                AllowedProxyHosts = ["proxy.example.test"],
            },
        };
        OutboundProxyLease proxy = await broker.AcquireAsync(profile, destination, "test");
        var lease = new IntegrationFetchLease(new HttpClient(new FailingDisposalHandler()), destination,
            cookies, true, "test", account, proxy, TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.DisposeAsync().AsTask());
        accounts.Should().Be(1);
        host.Http.Calls.Should().HaveCount(2);
        await lease.DisposeAsync();
        accounts.Should().Be(1);
        host.Http.Calls.Should().HaveCount(2);
    }

    private sealed class FailingDisposalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing) => throw new InvalidOperationException("Synthetic disposal failure.");
    }
}
