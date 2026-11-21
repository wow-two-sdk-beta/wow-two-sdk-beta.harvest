using System.Diagnostics;
using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Capabilities;

public sealed class PostgresHostFetchStoreTests
{
    [Fact]
    public async Task AcquireAsync_ShouldKeepHostStateAnd304Content_AcrossLeaseOwners()
    {
        await using var host = await ModuleHost.StartAsync();
        var source = host.Services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
        var firstStore = new PostgresHostFetchStore(source, host.Clock);
        var due = host.Clock.GetUtcNow().AddMinutes(30);
        await using (var first = await firstStore.AcquireAsync("example.com", CancellationToken.None))
        {
            first.State.NextAllowedAt = due;
            first.State.PausedUntil = due;
            first.State.PauseReason = "Challenge";
            first.State.RobotsBody = "User-agent: *\nDisallow: /private";
            first.State.RobotsOrigin = "https://example.com";
            first.State.RobotsStatus = 200;
            first.State.RobotsCachedAt = host.Clock.GetUtcNow();
            await first.SaveAsync(CancellationToken.None);
            await first.SaveValidatorsAsync("https://example.com/event", new("\"v1\"", host.Clock.GetUtcNow(), Encoding.UTF8.GetBytes("event"), "text/html; charset=utf-8"), CancellationToken.None);
        }

        await using var second = await new PostgresHostFetchStore(source, host.Clock).AcquireAsync("example.com", CancellationToken.None);
        second.State.NextAllowedAt.Should().Be(due);
        second.State.PausedUntil.Should().Be(due);
        second.State.RobotsOrigin.Should().Be("https://example.com");
        var validators = await second.GetValidatorsAsync("https://example.com/event", CancellationToken.None);
        validators!.ETag.Should().Be("\"v1\"");
        Encoding.UTF8.GetString(validators.Body!).Should().Be("event");
    }

    [Fact]
    public async Task AcquireAsync_ShouldReadTheNearestParentHostPolicy()
    {
        await using var host = await ModuleHost.StartAsync();
        await host.ScalarAsync<int>(
            """
            WITH inserted AS (INSERT INTO harvest_host_policies(host_key, permission, updated_at)
                VALUES ('example.com', 'denied', now()) RETURNING 1) SELECT count(*)::integer FROM inserted
            """);
        var source = host.Services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
        await using var lease = await new PostgresHostFetchStore(source, host.Clock).AcquireAsync("events.example.com", CancellationToken.None);
        lease.State.PolicyPermission.Should().Be("denied");
    }

    [Fact]
    public async Task AcquireAsync_ShouldReleaseTheAdvisoryLock_WhenAWaitIsCancelled()
    {
        await using var host = await ModuleHost.StartAsync();
        var source = host.Services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
        var store = new PostgresHostFetchStore(source, host.Clock);
        await using (var first = await store.AcquireAsync("example.com", CancellationToken.None))
        {
            using var cancellation = new CancellationTokenSource();
            var waiting = store.AcquireAsync("example.com", cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var next = await store.AcquireAsync("example.com", timeout.Token);
        next.State.Permission.Should().Be("open");
    }

    [Fact]
    public async Task PacedRequests_ShouldReserveStartsOneGapApartThroughTheRealLock_AndOverlapInFlight()
    {
        await using var host = await ModuleHost.StartAsync();
        var source = host.Services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
        var store = new PostgresHostFetchStore(source, TimeProvider.System);
        var gap = TimeSpan.FromMilliseconds(400);
        var clock = Stopwatch.StartNew();
        var starts = new List<long>();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ScriptedTransport(async (_, cancellation) =>
        {
            int index;
            lock (starts)
            {
                starts.Add(clock.ElapsedMilliseconds);
                index = starts.Count;
            }
            // The first reply waits for the second request to start, so the two overlap in flight; with the lock held through a
            // request the second could never start while the first waits.
            if (index == 1)
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellation);
            else
                secondStarted.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content") };
        });
        var routes = new Routes(transport);

        async Task<FetchOutcome> SendAsync()
        {
            using var client = new HttpClient(new HarvestFetchHandler(routes, store, new HarvestFetchOptions(),
                new GenericChallengeDetector(TimeProvider.System), TimeProvider.System));
            using var message = new HttpRequestMessage(HttpMethod.Get, "https://paced.example/event");
            message.Options.Set(HarvestFetchMetadata.RequestKey, new FetchRequest
            {
                Uri = new Uri("https://paced.example/event"), RobotsPolicy = RobotsPolicy.Ignore, Pace = new RequestPace { Gap = gap },
            });
            using var response = await client.SendAsync(message);
            message.Options.TryGetValue(HarvestFetchMetadata.ResponseKey, out var metadata);
            return metadata!.Outcome;
        }

        var first = SendAsync();
        while (true)
        {
            lock (starts)
            {
                if (starts.Count > 0) break;
            }
            await Task.Delay(5);
        }
        var second = SendAsync();
        (await Task.WhenAll(first, second)).Should().OnlyContain(outcome => outcome == FetchOutcome.Ok);

        starts.Should().HaveCount(2);
        (starts[1] - starts[0]).Should().BeGreaterThanOrEqualTo((long)gap.TotalMilliseconds - 50, "the second start waits for the first reserved gap");
        await using var lease = await store.AcquireAsync("paced.example", CancellationToken.None);
        lease.State.ConsecutiveFailures.Should().Be(0);
        lease.State.PausedUntil.Should().BeNull();
        (lease.State.NextAllowedAt - DateTimeOffset.UtcNow).Should().BeLessThanOrEqualTo(gap + TimeSpan.FromMilliseconds(100), "the reserved starts are one gap apart, not stacked");
    }

    [Fact]
    public async Task FetchAsync_ShouldRejectLoopbackThroughTheRealGuardedTransport()
    {
        await using var host = await ModuleHost.StartAsync();
        var source = host.Services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey);
        var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System);
        services.AddKeyedSingleton(HarvestModuleConstants.ServiceKey, source);
        services.AddHarvestFetching();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var response = await provider.GetRequiredService<IPageFetcher>().FetchAsync(new FetchRequest
        {
            Uri = new Uri("http://127.0.0.1:1/private"), RobotsPolicy = RobotsPolicy.Ignore,
        });
        response.Outcome.Should().Be(FetchOutcome.InvalidRedirect);
    }

    private sealed class ScriptedTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> script) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            script(request, cancellationToken);
    }

    private sealed class Routes(HttpMessageHandler handler) : IFetchTransportFactory
    {
        public ValueTask<IFetchTransportLease> AcquireAsync(Uri destination, System.Net.CookieContainer? runCookies = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IFetchTransportLease>(new Route(new HttpClient(handler, disposeHandler: false), destination));
    }

    private sealed class Route(HttpClient client, Uri origin) : IFetchTransportLease
    {
        public HttpClient Client => client;
        public System.Net.CookieContainer? Cookies => null;
        public bool IsPrivate => false;
        public string RouteKey => "test-route";
        public bool Allows(Uri destination) => destination.IdnHost == origin.IdnHost;
        public ValueTask DisposeAsync()
        {
            client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
