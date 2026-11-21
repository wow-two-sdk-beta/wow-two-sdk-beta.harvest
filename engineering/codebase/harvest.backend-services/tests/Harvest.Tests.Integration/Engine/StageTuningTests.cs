using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Exercises a stage's own tuning end to end: the catalog walks, the harvest batch and budgets, the skipped empty slot and
/// the claims of a run that fetches in parallel.</summary>
public sealed class StageTuningTests
{
    private const string Host = "mstar.uz";
    private const string CatalogType = "application/x-fixture-catalog";

    // ── Catalog walks ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullWalk_ShouldRestartAtTheEntryPage_ReadToTheLastPage_AndKeepKnownAdsCaptured()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxRequests = 3 },
            schedules: [Discover(DiscoveryWalk.Full), Harvest(batch: 1)]);
        PublishCatalog(host);

        var first = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        first.Status.Should().Be(RunStatus.Completed);
        first.CompletionReason.Should().Be(RunReason.Exhausted);
        first.Pages.Should().Be(5, "the pipeline's request budget of 3 does not cut a full walk");
        first.Discovered.Should().Be(10);
        PageCalls(host).Should().Equal(Pages(5));

        (await RunAsync(host, registration.PipelineId, WorkKind.Harvest)).Captured.Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries WHERE detail_state='captured'")).Should().Be(1);
        string known = await host.ScalarAsync<string>("SELECT url FROM harvest_discoveries WHERE detail_state='captured'");
        host.Fetcher.Calls.Clear();

        var second = await RunAsync(host, registration.PipelineId, WorkKind.Discover);

        second.Status.Should().Be(RunStatus.Completed);
        second.CompletionReason.Should().Be(RunReason.Exhausted);
        second.Pages.Should().Be(5);
        PageCalls(host).Should().Equal(Pages(5), "the second run starts at the entry page again");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries")).Should().Be(10);
        (await host.ScalarAsync<string>("SELECT url FROM harvest_discoveries WHERE detail_state='captured'")).Should().Be(known);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries WHERE detail_state='pending'")).Should().Be(9);
    }

    [Fact]
    public async Task FullWalk_ShouldStopAtTheTimeBudget_WhichStillBindsIt()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxRequests = 3, MaxDuration = TimeSpan.FromSeconds(30) },
            schedules: [Discover(DiscoveryWalk.Full)]);
        PublishCatalog(host);
        foreach (string page in Pages(5))
        {
            string body = CatalogBody(page, 5, 2);
            host.Fetcher.Set(page, (_, _) =>
            {
                host.Clock.Advance(TimeSpan.FromSeconds(20)); // Within the 30 s claim lease, past half of the 30 s time budget.
                return Task.FromResult(host.Fetcher.Response(page, body, CatalogType));
            });
        }

        var run = await RunAsync(host, registration.PipelineId, WorkKind.Discover);

        run.Status.Should().Be(RunStatus.Partial);
        run.CompletionReason.Should().Be(RunReason.TimeLimit);
        run.Pages.Should().BeLessThan(5);
    }

    [Fact]
    public async Task ResumeWalk_ShouldKeepTodaysCursorAndRequestBudget()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxRequests = 3 }, schedules: [Discover(walk: null)]);
        PublishCatalog(host);

        var first = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        first.Status.Should().Be(RunStatus.Partial);
        first.CompletionReason.Should().Be(RunReason.RequestLimit);
        PageCalls(host).Should().Equal(Pages(3));
        host.Fetcher.Calls.Clear();

        var second = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        second.Status.Should().Be(RunStatus.Completed);
        PageCalls(host).Should().Equal(Pages(5).Skip(3), "the next run resumes at the cursor");
        host.Fetcher.Calls.Clear();

        var third = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        third.CompletionReason.Should().Be(RunReason.RequestLimit);
        PageCalls(host).Should().Equal(Pages(3), "a finished cycle starts at page 1 again");
    }

    [Fact]
    public async Task NewWalk_ShouldReadEveryPageWhileEachHoldsSomethingNew_AndStopAtTheFirstPageThatDoesNot()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxRequests = 3 }, schedules: [Discover(DiscoveryWalk.New)]);
        PublishCatalog(host);

        var everyPageNew = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        everyPageNew.Status.Should().Be(RunStatus.Completed);
        everyPageNew.Pages.Should().Be(5, "every page held something new, and the request budget does not cut the walk");
        host.Fetcher.Calls.Clear();

        // A newest-first catalog gains one ad on its first page; page 2 holds only ads already held.
        string[] pages = Pages(5);
        host.Fetcher.Set(pages[0], CatalogBody(pages[0], 5, 2, extra: [$"https://{Host}/events/11"]), CatalogType);
        var stopped = await RunAsync(host, registration.PipelineId, WorkKind.Discover);

        stopped.Status.Should().Be(RunStatus.Completed);
        stopped.CompletionReason.Should().Be(RunReason.Exhausted);
        stopped.Pages.Should().Be(2);
        PageCalls(host).Should().Equal(pages.Take(2));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries")).Should().Be(11);
        host.Fetcher.Calls.Clear();

        // Nothing is new anywhere: the first page alone ends the walk.
        var quiet = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        quiet.Pages.Should().Be(1);
        PageCalls(host).Should().Equal(pages.Take(1));
    }

    [Fact]
    public async Task NewWalk_ShouldCountAKnownAdThatAChangedFingerprintPutBackToPendingAsNew()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(schedules: [Discover(DiscoveryWalk.New), Harvest(batch: 10)]);
        PublishCatalog(host);
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        (await RunAsync(host, registration.PipelineId, WorkKind.Harvest)).Captured.Should().Be(10);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries WHERE detail_state='captured'")).Should().Be(10);
        host.Fetcher.Calls.Clear();

        // Every ad is held and captured, so the first page holds nothing new and the walk ends there.
        string[] pages = Pages(5);
        (await RunAsync(host, registration.PipelineId, WorkKind.Discover)).Pages.Should().Be(1);
        PageCalls(host).Should().Equal(pages.Take(1));
        host.Fetcher.Calls.Clear();

        // The first page's first ad changed upstream: its fingerprint puts it back to pending, which counts as new.
        host.Fetcher.Set(pages[0], CatalogBody(pages[0], 5, 2, modified: new Dictionary<int, string> { [1] = "2026-11-01T09:00:00Z" }), CatalogType);
        var run = await RunAsync(host, registration.PipelineId, WorkKind.Discover);

        run.Status.Should().Be(RunStatus.Completed);
        PageCalls(host).Should().Equal(pages.Take(2), "page 1 held a changed ad, so page 2 was read; it held nothing new");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries WHERE detail_state='pending'")).Should().Be(1);
    }

    // ── Budgets ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Harvest_ShouldCaptureItsWholeBatchOf100_UnderAPipelineRequestBudgetOf70()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxRequests = 70 },
            schedules: [Discover(walk: null), Harvest(batch: 100)]);
        host.Fetcher.Set($"https://{Host}/sitemap.xml", CatalogBody($"https://{Host}/sitemap.xml", 1, 100), CatalogType);
        for (int index = 1; index <= 100; index++)
            host.Fetcher.Set($"https://{Host}/events/{index}", Event(index));
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);

        var run = await RunAsync(host, registration.PipelineId, WorkKind.Harvest);

        run.Captured.Should().Be(100);
        run.Status.Should().Be(RunStatus.Completed);
        run.Requests.Should().Be(100);
    }

    // ── Empty and fast harvest slots ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduledHarvestSlot_ShouldInsertNoRunWhenNothingIsDue_AndStillAdvanceItsNextDueTime()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(schedules: [Harvest(batch: 5, every: TimeSpan.FromSeconds(15))]);
        using var scope = host.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();

        (await engine.QueueDueAsync()).Should().Be(0);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@p", ("p", registration.PipelineId))).Should().Be(0);
        (await host.ScalarAsync<DateTimeOffset>("SELECT next_due_at FROM harvest_schedules WHERE pipeline_id=@p AND kind='harvest'",
            ("p", registration.PipelineId))).Should().Be(host.Clock.GetUtcNow().AddSeconds(15));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_schedules WHERE pipeline_id=@p AND last_queued_at IS NOT NULL",
            ("p", registration.PipelineId))).Should().Be(0, "no run was queued, so the schedule does not look freshly queued");

        // A manual harvest still records that it had nothing to do.
        var manual = await RunAsync(host, registration.PipelineId, WorkKind.Harvest);
        manual.Status.Should().Be(RunStatus.Completed);
        manual.CompletionReason.Should().Be(RunReason.NoItems);
    }

    [Fact]
    public async Task ScheduledHarvest_ShouldQueueTwoRunsFifteenSecondsApart_AndNoneOnceNothingIsLeft()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(schedules: [Discover(walk: null), Harvest(batch: 1, every: TimeSpan.FromSeconds(15))]);
        host.Fetcher.Set($"https://{Host}/sitemap.xml", CatalogBody($"https://{Host}/sitemap.xml", 1, 2), CatalogType);
        for (int index = 1; index <= 2; index++)
            host.Fetcher.Set($"https://{Host}/events/{index}", Event(index));
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        using var scope = host.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        host.Clock.Advance(TimeSpan.FromSeconds(30));

        (await engine.QueueDueAsync()).Should().Be(1);
        await DrainAsync(host);
        host.Clock.Advance(TimeSpan.FromSeconds(15));
        (await engine.QueueDueAsync()).Should().Be(1);
        await DrainAsync(host);
        host.Clock.Advance(TimeSpan.FromSeconds(15));
        (await engine.QueueDueAsync()).Should().Be(0, "every discovery is captured");

        var slots = await host.ScalarAsync<DateTimeOffset[]>(
            "SELECT array_agg(scheduled_for ORDER BY scheduled_for) FROM harvest_runs WHERE pipeline_id=@p AND kind='harvest' AND trigger='schedule'",
            ("p", registration.PipelineId));
        slots.Should().HaveCount(2);
        (slots[1] - slots[0]).Should().Be(TimeSpan.FromSeconds(15));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries WHERE detail_state='captured'")).Should().Be(2);
    }

    // ── Claims ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParallelRun_ShouldClaimThreeItemsOnOneHostLane_WhileADefaultRunBesideItClaimsItsOne()
    {
        await using var host = await StartAsync();
        var parallel = await host.RegisterAsync(schedules: [Discover(walk: null), Harvest(batch: 5, parallel: 3, gapMs: 250)]);
        var plain = await host.RegisterAsync(schedules: [Discover(walk: null), Harvest(batch: 5)]);
        host.Fetcher.Set($"https://{Host}/sitemap.xml", CatalogBody($"https://{Host}/sitemap.xml", 1, 5), CatalogType);
        for (int index = 1; index <= 5; index++)
            host.Fetcher.Set($"https://{Host}/events/{index}", Event(index));
        await RunAsync(host, parallel.PipelineId, WorkKind.Discover);
        await RunAsync(host, plain.PipelineId, WorkKind.Discover);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        EngineHost.Data(await runs.StartAsync(parallel.PipelineId, WorkKind.Harvest));
        EngineHost.Data(await runs.StartAsync(plain.PipelineId, WorkKind.Harvest));
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        var claims = new List<HarvestEngine.WorkClaim>();

        var shared = await engine.ClaimAsync(HarvestEngine.ClaimPool.Shared, CancellationToken.None);
        shared.Should().NotBeNull();
        shared!.Frozen.Pipeline.Id.Should().Be(plain.PipelineId, "the default run draws from the shared pool");
        (await engine.ClaimAsync(HarvestEngine.ClaimPool.Shared, CancellationToken.None))
            .Should().BeNull("one claim per host lane across the shared pool's runs; the parallel run is not in it");
        for (int attempt = 0; attempt < 5; attempt++)
            if (await engine.ClaimAsync(HarvestEngine.ClaimPool.Parallel, CancellationToken.None) is { } claim)
                claims.Add(claim);

        claims.Should().HaveCount(3);
        claims.Should().OnlyContain(claim => claim.Frozen.Pipeline.Id == parallel.PipelineId);
        claims.Select(claim => claim.Item.WorkItemId).Should().OnlyHaveUniqueItems();
        claims.Select(claim => claim.Item.HostKey + "/" + claim.Item.Lane).Distinct().Should().ContainSingle()
            .Which.Should().Be(shared.Item.HostKey + "/" + shared.Item.Lane);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item WHERE pipeline_id=@p AND state='running'", ("p", parallel.PipelineId)))
            .Should().Be(3);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item WHERE pipeline_id=@p AND state='running'", ("p", plain.PipelineId)))
            .Should().Be(1);

        foreach (var claim in claims.Append(shared))
            await engine.ExecuteAsync(claim, CancellationToken.None);
        (await engine.ClaimAsync(HarvestEngine.ClaimPool.Parallel, CancellationToken.None)).Should().NotBeNull("finished claims free the run's slots");
    }

    [Fact]
    public async Task WorkItemsOfAGapOrParallelRun_ShouldCarryTheirPaceInThePlan_AndOtherRunsNone()
    {
        await using var host = await StartAsync();
        var gapped = await host.RegisterAsync(schedules: [Discover(walk: null, gapMs: 100), Harvest(batch: 2, gapMs: 250)]);
        var parallel = await host.RegisterAsync(schedules: [Discover(walk: null), Harvest(batch: 2, parallel: 3)]);
        var plain = await host.RegisterAsync(schedules: [Discover(walk: null), Harvest(batch: 2)]);
        host.Fetcher.Set($"https://{Host}/sitemap.xml", CatalogBody($"https://{Host}/sitemap.xml", 1, 2), CatalogType);
        foreach (var registration in new[] { gapped, parallel, plain })
        {
            await RunAsync(host, registration.PipelineId, WorkKind.Discover);
            await RunAsync(host, registration.PipelineId, WorkKind.Harvest, drain: false);
        }

        async Task<ResolvedPlan[]> Plans(Guid pipeline, string kind)
        {
            var json = await host.ScalarAsync<string[]>(
                "SELECT array_agg(w.plan_ref::text) FROM harvest_work_item w JOIN harvest_runs r ON r.id=w.run_id WHERE w.pipeline_id=@p AND r.kind=@kind",
                ("p", pipeline), ("kind", kind));
            return [.. json.Select(text => JsonSerializer.Deserialize<ResolvedPlan>(text, JsonOptionsConstants.Default)!)];
        }

        var discoverGap = await Plans(gapped.PipelineId, "discover");
        discoverGap.Should().OnlyContain(plan => plan.Requirements.Pace != null && plan.Requirements.Pace.Gap == TimeSpan.FromMilliseconds(100));
        var gap = await Plans(gapped.PipelineId, "harvest");
        gap.Should().HaveCount(2).And.OnlyContain(plan => plan.Requirements.Pace != null && plan.Requirements.Pace.Gap == TimeSpan.FromMilliseconds(250));
        var parallelOnly = await Plans(parallel.PipelineId, "harvest");
        parallelOnly.Should().HaveCount(2).And.OnlyContain(plan => plan.Requirements.Pace != null && plan.Requirements.Pace.Gap == null);
        (await Plans(plain.PipelineId, "harvest")).Should().HaveCount(2).And.OnlyContain(plan => plan.Requirements.Pace == null);
        (await Plans(plain.PipelineId, "discover")).Should().OnlyContain(plan => plan.Requirements.Pace == null);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<EngineHost> StartAsync() => EngineHost.StartAsync(services =>
        services.Insert(0, ServiceDescriptor.Singleton<ICaptureReader>(new CatalogReader())));

    private static ScheduleDeclaration Discover(DiscoveryWalk? walk, int? gapMs = null) => new()
    {
        Kind = WorkKind.Discover, Mode = ScheduleMode.Manual, Stage = new() { BatchSize = 10, Walk = walk, GapMs = gapMs },
    };

    private static ScheduleDeclaration Harvest(int batch, TimeSpan? every = null, int? parallel = null, int? gapMs = null) => new()
    {
        Kind = WorkKind.Harvest, Mode = every is null ? ScheduleMode.Manual : ScheduleMode.Interval, Every = every,
        Stage = new() { BatchSize = batch, Parallel = parallel, GapMs = gapMs },
    };

    /// <summary>Gets the catalog's page addresses: the entry page, then the numbered pages.</summary>
    private static string[] Pages(int count) =>
        [$"https://{Host}/sitemap.xml", .. Enumerable.Range(2, count - 1).Select(page => $"https://{Host}/catalog/{page}")];

    private static IEnumerable<string> PageCalls(EngineHost host) =>
        host.Fetcher.Calls.Where(call => !call.Contains("/events/", StringComparison.Ordinal));

    /// <summary>Publishes a 5-page catalog of 2 ads a page, each page naming the next.</summary>
    private static void PublishCatalog(EngineHost host)
    {
        foreach (string page in Pages(5))
            host.Fetcher.Set(page, CatalogBody(page, 5, 2), CatalogType);
        for (int index = 1; index <= 11; index++)
            host.Fetcher.Set($"https://{Host}/events/{index}", Event(index));
    }

    /// <summary>Builds one catalog page: its ads, one line each, and the next page's address unless it is the last.</summary>
    private static string CatalogBody(string page, int pages, int perPage, string[]? extra = null, Dictionary<int, string>? modified = null)
    {
        int number = Array.IndexOf(Pages(pages), page) + 1;
        var lines = new List<string>();
        foreach (string url in extra ?? [])
            lines.Add("listing " + url);
        for (int offset = 1; offset <= perPage; offset++)
        {
            int index = (number - 1) * perPage + offset;
            lines.Add($"listing https://{Host}/events/{index}" + (modified?.TryGetValue(index, out string? stamp) == true ? " " + stamp : ""));
        }
        if (number < pages)
            lines.Add("next " + Pages(pages)[number]);
        return string.Join('\n', lines);
    }

    private static string Event(int index) => $$"""
        <html lang="en"><script type="application/ld+json">{"@type":"Event","@id":"#event",
        "name":"The fixture {{index}}","description":"An event with music.","startDate":"2026-11-01T19:00:00+05:00"}</script></html>
        """;

    private static async Task<RunModel> RunAsync(EngineHost host, Guid pipelineId, WorkKind kind, bool drain = true)
    {
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var queued = EngineHost.Data(await runs.StartAsync(pipelineId, kind));
        if (drain)
            await DrainAsync(host);
        return EngineHost.Data(await runs.GetAsync(queued.Id));
    }

    private static async Task DrainAsync(EngineHost host)
    {
        for (int claimed = 0; await host.ProcessAsync(); claimed++)
            if (claimed > 1000) throw new InvalidOperationException("The fixture queue did not drain.");
    }

    /// <summary>Reads the fixture catalog format: <c>listing &lt;url&gt; [lastmod]</c> and <c>next &lt;url&gt;</c> lines.</summary>
    private sealed class CatalogReader : ICaptureReader
    {
        public string Key => "fixture-catalog";

        public bool CanRead(FetchedDocument document) => document.ContentType == CatalogType;

        public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
        {
            var discovered = Encoding.UTF8.GetString(document.Body.Span).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split(' '))
                .Select(parts => new DiscoveredUrl
                {
                    Url = parts[1], IsListing = parts[0] == "next",
                    LastModifiedUtc = parts.Length > 2 ? DateTimeOffset.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : null,
                }).ToList();
            return Task.FromResult(Result<CaptureReadResult>.Ok(new() { Discovered = discovered, Complete = true }));
        }
    }
}
