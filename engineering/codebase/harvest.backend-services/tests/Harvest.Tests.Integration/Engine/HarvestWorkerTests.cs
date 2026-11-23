using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Runs the real <see cref="HarvestWorker"/> over the real engine and PostgreSQL, with fixture fetches and the worker's
/// own clock, and watches how often it ticks and how many claims it runs at once.</summary>
public sealed class HarvestWorkerTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task IdleWorker_ShouldWaitThePollIntervalBetweenTicks_AfterAClaimHasFinished()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules: [DiscoverSchedule()]);
        host.Fetcher.Set("https://mstar.uz/sitemap.xml", "<urlset><url><loc>https://mstar.uz/events/1</loc></url></urlset>", "application/xml");
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        var ticks = new TickCounter(host.Services.GetRequiredService<IServiceScopeFactory>());
        using var worker = NewWorker(ticks, parallelSlots: 4);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(async () => EngineHost.Data(await runs.GetAsync(run.Id)).Status == RunStatus.Completed, "the claim to finish");
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            int before = ticks.Created;

            await Task.Delay(TimeSpan.FromMilliseconds(1500));

            // Each tick opens one scope, so a worker that waits the poll interval ticks about five times in 1.5 s; one that
            // re-queries the queue at once with no wait ticks hundreds of times.
            int idle = ticks.Created - before;
            idle.Should().BeInRange(1, 12, "an idle worker waits the poll interval between its ticks, finished claims or not");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WorkerWithNoParallelRun_ShouldRunOneClaimAtATime_AndTakeEveryClaim()
    {
        await using var host = await EngineHost.StartAsync();
        var inFlight = new Gauge();
        string[] sites = ["alpha.uz", "bravo.uz", "charlie.uz"];
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var started = new List<Guid>();
        foreach (string site in sites)
        {
            var registration = await host.RegisterAsync(site, schedules: [DiscoverSchedule()]);
            string url = $"https://{site}/sitemap.xml";
            host.Fetcher.Set(url, async (_, token) =>
            {
                await inFlight.HoldAsync(TimeSpan.FromMilliseconds(250), token);
                return host.Fetcher.Response(url, "<urlset></urlset>", "application/xml");
            });
            started.Add(EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover)).Id);
        }
        var ticks = new TickCounter(host.Services.GetRequiredService<IServiceScopeFactory>());
        using var worker = NewWorker(ticks, parallelSlots: 4);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(async () => (await Task.WhenAll(started.Select(async id => EngineHost.Data(await runs.GetAsync(id)).Status)))
                .All(status => status == RunStatus.Completed), "the three runs to finish");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        host.Fetcher.Calls.Should().BeEquivalentTo(sites.Select(site => $"https://{site}/sitemap.xml"));
        inFlight.Peak.Should().Be(1, "with no parallel run only the shared slot works, one claim at a time");
    }

    [Fact]
    public async Task WorkerWithAParallelRun_ShouldFetchItsItemsAtOnce()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules:
        [
            DiscoverSchedule(),
            new() { Kind = WorkKind.Harvest, Mode = ScheduleMode.Manual, Stage = new() { BatchSize = 3, Parallel = 3 } },
        ]);
        host.Fetcher.Set("https://mstar.uz/sitemap.xml", "<urlset>" + string.Concat(Enumerable.Range(1, 3)
            .Select(index => $"<url><loc>https://mstar.uz/events/{index}</loc></url>")) + "</urlset>", "application/xml");
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        (await host.DrainAsync()).Should().Be(1);
        var inFlight = new Gauge();
        for (int index = 1; index <= 3; index++)
        {
            string url = $"https://mstar.uz/events/{index}";
            host.Fetcher.Set(url, async (_, token) =>
            {
                await inFlight.HoldAsync(TimeSpan.FromMilliseconds(400), token);
                return host.Fetcher.Response(url, "<html></html>");
            });
        }
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var harvest = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Harvest));
        var ticks = new TickCounter(host.Services.GetRequiredService<IServiceScopeFactory>());
        using var worker = NewWorker(ticks, parallelSlots: 4);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForAsync(async () => EngineHost.Data(await runs.GetAsync(harvest.Id)).Status
                is not (RunStatus.Queued or RunStatus.Running), "the harvest run to end");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        inFlight.Peak.Should().BeGreaterThan(1, "a run that sets parallel fetches runs its claims in the parallel slots");
    }

    private static HarvestWorker NewWorker(IServiceScopeFactory scopes, int parallelSlots) => new(scopes,
        Options.Create(new HarvestEngineOptions { Enabled = true, PollInterval = Poll, ParallelSlots = parallelSlots }),
        TimeProvider.System, NullLogger<HarvestWorker>.Instance);

    private static ScheduleDeclaration DiscoverSchedule() => new() { Kind = WorkKind.Discover, Mode = ScheduleMode.Manual, Stage = new() };

    private static async Task WaitForAsync(Func<Task<bool>> done, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await done())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for " + what + ".");
            await Task.Delay(25);
        }
    }

    /// <summary>Counts the scopes the worker opens; it opens one for each tick and one for each claim it runs.</summary>
    private sealed class TickCounter(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int created;

        internal int Created => Volatile.Read(ref created);

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref created);
            return inner.CreateScope();
        }
    }

    /// <summary>Holds a fetch for a while and records how many fetches were held at once.</summary>
    private sealed class Gauge
    {
        private int current;
        private int peak;

        internal int Peak => Volatile.Read(ref peak);

        internal async Task HoldAsync(TimeSpan time, CancellationToken token)
        {
            int now = Interlocked.Increment(ref current);
            int seen;
            while ((seen = Volatile.Read(ref peak)) < now && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            {
            }
            try
            {
                await Task.Delay(time, token);
            }
            finally
            {
                Interlocked.Decrement(ref current);
            }
        }
    }
}
