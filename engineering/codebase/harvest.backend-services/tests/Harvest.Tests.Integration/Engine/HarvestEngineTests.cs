using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Exercises the engine against real durable stores and offline source fixtures.</summary>
public sealed class HarvestEngineTests
{
    [Fact]
    public async Task EditingAFilter_ShouldPreserveQueuedScopeAndUseNewScopeForTheNextRun()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(family: SourceFamily.Structured,
            pipelineScope: new() { Url = "https://mstar.uz/old?city=1" });
        host.Fetcher.Set("https://mstar.uz/old?city=1", Event("mstar.uz", 1));
        host.Fetcher.Set("https://mstar.uz/new?city=2", Event("mstar.uz", 2));
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        EngineHost.Data(await registry.UpdatePipelineAsync(registration.PipelineId, new()
        {
            Scope = new() { Url = "https://mstar.uz/new?city=2" }, Schedules = [],
        }));
        await host.DrainAsync();
        host.Fetcher.Calls.Should().Equal("https://mstar.uz/old?city=1");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        await host.DrainAsync();
        host.Fetcher.Calls.Should().Equal("https://mstar.uz/old?city=1", "https://mstar.uz/new?city=2");
    }

    [Fact]
    public async Task DisablingASchedule_ShouldStopNewScheduledRunsAndKeepItsIdentifier()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules:
            [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(5) }]);
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var before = EngineHost.Data(await registry.GetPipelineAsync(registration.PipelineId));
        var after = EngineHost.Data(await registry.UpdatePipelineAsync(registration.PipelineId, new() { Scope = before.Scope, Schedules = [] }));
        after.Schedules.Single().Id.Should().Be(before.Schedules.Single().Id);
        (await scope.ServiceProvider.GetRequiredService<HarvestEngine>().QueueDueAsync()).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs")).Should().Be(0);
    }

    [Fact]
    public async Task DiscoveringANewMissingPage_ShouldFinishWithoutCreatingADeletionForAnUnknownLink()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 1);
        host.Fetcher.Set("https://mstar.uz/events/1", string.Empty, status: 404);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        (await host.DrainAsync()).Should().Be(2);
        EngineHost.Data(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Completed);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_link_events")).Should().Be(0);
    }

    [Fact]
    public async Task Scheduler_ShouldRejectAMismatchedSourceHostWithoutCreatingAnOrphanRun()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(pipelineScope: new() { Url = "https://other.example/sitemap.xml" }, schedules:
        [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(5) }]);
        using var scope = host.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<HarvestEngine>().QueueDueAsync()).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@pipeline", ("pipeline", registration.PipelineId))).Should().Be(0);
        host.Fetcher.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Capture_ShouldApplyScopeParametersToTheActualFetchTarget()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(family: SourceFamily.Structured, pipelineScope: new()
        {
            Url = "https://mstar.uz/event?lang=ru", Parameters = new Dictionary<string, string> { ["lang"] = "uz", ["city"] = "Toshkent markaz" },
        });
        const string target = "https://mstar.uz/event?city=Toshkent%20markaz&lang=uz";
        host.Fetcher.Set(target, Event("mstar.uz", 1));
        using var scope = host.Services.CreateScope();
        EngineHost.Data(await scope.ServiceProvider.GetRequiredService<IRunBroker>().StartAsync(registration.PipelineId));
        (await host.DrainAsync()).Should().Be(1);
        host.Fetcher.Calls.Should().Equal(target);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Capture_ShouldRespectTheRemainingItemBudgetAcrossMultiEventPages(int maxItems)
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxItems = maxItems });
        host.Fetcher.Set("https://mstar.uz/sitemap.xml", "<urlset><url><loc>https://mstar.uz/events/1</loc></url><url><loc>https://mstar.uz/events/2</loc></url></urlset>", "application/xml");
        for (var page = 1; page <= 2; page++)
        {
            var first = $"{{\"@type\":\"Event\",\"@id\":\"#one\",\"name\":\"The fixture {page} one\",\"startDate\":\"2026-11-0{page}\"}}";
            var second = $"{{\"@type\":\"Event\",\"@id\":\"#two\",\"name\":\"The fixture {page} two\",\"startDate\":\"2026-11-0{page}\"}}";
            host.Fetcher.Set($"https://mstar.uz/events/{page}", $"<script type='application/ld+json'>[{first},{second}]</script>");
        }
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        await host.DrainAsync();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(maxItems);
        var complete = EngineHost.Data(await runs.GetAsync(run.Id));
        complete.Status.Should().Be(RunStatus.Partial);
        complete.CompletionReason.Should().Be(RunReason.ItemLimit);
    }

    [Theory]
    [InlineData("mstar.uz")]
    [InlineData("voyage.uz")]
    public async Task SitemapCapture_ShouldDeliverEachFixtureOnceAndSkipUnchangedContent(string site)
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(site);
        Fixtures(host, site, count: 2);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        var first = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        (await host.DrainAsync()).Should().Be(3);
        var complete = EngineHost.Data(await runs.GetAsync(first.Id));
        complete.Status.Should().Be(RunStatus.Completed);
        complete.Captured.Should().Be(2);
        var page = await PullExpectedAsync(delivery, registration.SubscriptionId, 2);
        page.Data.Count(item => item.Type == "post.new").Should().Be(2);
        page.Data.Should().OnlyHaveUniqueItems(item => item.ItemId);
        page.Data.Should().OnlyContain(item => item.Data != null);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(2);

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        second.Id.Should().NotBe(first.Id);
        (await host.DrainAsync()).Should().Be(3);
        var reread = EngineHost.Data(await runs.GetAsync(second.Id));
        reread.Captured.Should().Be(0);
        reread.Unchanged.Should().Be(2);
        EngineHost.Data(await delivery.PullAsync(registration.SubscriptionId, page.Next)).Data.Should().BeEmpty();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(2);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE type='post.new'")).Should().Be(2);
    }

    [Fact]
    public async Task Check_ShouldMoveAMissingFixtureFromLiveToSuspectThenGone()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 1);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        await host.DrainAsync();
        var post = EngineHost.Data(await posts.ListPostsAsync(registration.ContainerId)).Single();
        post.State.Should().Be(PostState.Live);
        host.Fetcher.Set("https://mstar.uz/events/1", string.Empty, status: 404);

        host.Clock.Advance(TimeSpan.FromHours(6));
        EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Check));
        (await host.DrainAsync()).Should().Be(1);
        EngineHost.Data(await posts.GetPostAsync(registration.ContainerId, post.Id)).State.Should().Be(PostState.Suspect);

        host.Clock.Advance(TimeSpan.FromHours(6));
        EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Check));
        await host.DrainAsync();
        EngineHost.Data(await posts.GetPostAsync(registration.ContainerId, post.Id)).State.Should().Be(PostState.Gone);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_link_events WHERE to_state IN ('suspect','gone')")).Should().Be(2);
    }

    [Fact]
    public async Task ReclaimedLease_ShouldResumeADurableSweepWithoutDuplicateCaptures()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 2);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        (await host.ProcessAsync()).Should().BeTrue(); // Enumeration has durably committed its frontier.
        await host.ExecuteAsync("""
            UPDATE harvest_work_item SET state='running',lease_owner='dead-worker',lease_until=@until,attempts=1
            WHERE id=(SELECT id FROM harvest_work_item WHERE run_id=@run AND step='detail' ORDER BY id LIMIT 1)
            """, ("until", host.Clock.GetUtcNow().AddSeconds(30)), ("run", run.Id));
        (await host.ProcessAsync()).Should().BeFalse("the abandoned host claim is still leased");
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        (await host.DrainAsync()).Should().Be(2);
        EngineHost.Data(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Completed);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item WHERE run_id=@run AND state='done'", ("run", run.Id))).Should().Be(3);
        (await host.ScalarAsync<int>("SELECT max(attempts) FROM harvest_work_item WHERE run_id=@run", ("run", run.Id))).Should().Be(2);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(2);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE type='post.new'")).Should().Be(2);
    }

    [Fact]
    public async Task Challenge_ShouldRecordPartialRunAndPauseTheHostWithoutRetrying()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 2);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        await host.ProcessAsync();
        host.Fetcher.Set("https://mstar.uz/events/1", (_, _) => Task.FromResult(new WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching.FetchResponse
        {
            Outcome = FetchOutcome.Challenge, Challenge = ChallengeKind.CaptchaTurnstile, RetryAfter = TimeSpan.FromMinutes(20),
        }));
        // Pick the challenged target regardless of random UUIDv7 suffix ordering.
        await host.ExecuteAsync("UPDATE harvest_work_item SET priority=4 WHERE run_id=@run AND target='https://mstar.uz/events/1'", ("run", run.Id));
        (await host.ProcessAsync()).Should().BeTrue();
        (await host.ProcessAsync()).Should().BeFalse();
        var complete = EngineHost.Data(await runs.GetAsync(run.Id));
        complete.Status.Should().Be(RunStatus.Partial);
        complete.FetchOutcome.Should().Be(FetchOutcome.Challenge);
        host.Fetcher.Calls.Should().HaveCount(2);
        (await host.ScalarAsync<DateTimeOffset>("SELECT paused_until FROM harvest_host_lane WHERE host_key='mstar.uz'"))
            .Should().BeOnOrAfter(host.Clock.GetUtcNow().AddMinutes(20));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(0);
    }

    [Fact]
    public async Task ExpiredOwner_ShouldDiscardItsLateResultBeforeAReplacementCommits()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 1);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        await host.ProcessAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Fetcher.Set("https://mstar.uz/events/1", async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return host.Fetcher.Response("https://mstar.uz/events/1", Event("mstar.uz", 1));
        });
        var oldOwner = host.ProcessAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        release.SetResult();
        (await oldOwner).Should().BeTrue();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(0, "the completion fence rejects an expired owner");
        host.Fetcher.Set("https://mstar.uz/events/1", Event("mstar.uz", 1));
        (await host.ProcessAsync()).Should().BeTrue();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(1);
        EngineHost.Data(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Completed);
    }

    [Fact]
    public async Task Scheduler_ShouldQueueOneCurrentRunAndAdvanceItsNextSlot()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules:
        [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(5), TimeZone = "Asia/Tashkent" }]);
        Fixtures(host, "mstar.uz", count: 1);
        using var scope = host.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        (await engine.QueueDueAsync()).Should().Be(1);
        (await engine.QueueDueAsync()).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@pipeline", ("pipeline", registration.PipelineId))).Should().Be(1);
        (await host.ScalarAsync<DateTimeOffset>("SELECT next_due_at FROM harvest_schedules WHERE pipeline_id=@pipeline", ("pipeline", registration.PipelineId)))
            .Should().Be(host.Clock.GetUtcNow().AddMinutes(5));
        await host.DrainAsync();
        host.Clock.Advance(TimeSpan.FromMinutes(30));
        (await engine.QueueDueAsync()).Should().Be(1, "missed ticks coalesce rather than creating a backlog of six runs");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@pipeline", ("pipeline", registration.PipelineId))).Should().Be(2);
    }

    private static void Fixtures(EngineHost host, string site, int count)
    {
        host.Fetcher.Set($"https://{site}/sitemap.xml", "<urlset>" + string.Concat(Enumerable.Range(1, count)
            .Select(index => $"<url><loc>https://{site}/events/{index}</loc></url>")) + "</urlset>", "application/xml");
        for (var index = 1; index <= count; index++) host.Fetcher.Set($"https://{site}/events/{index}", Event(site, index));
    }

    private static async Task<CursorPage<DeliveryEvent>> PullExpectedAsync(IDeliveryBroker delivery, Guid subscription, int count)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var page = EngineHost.Data(await delivery.PullAsync(subscription));
            if (page.Data.Count >= count) return page;
            // Another parallel test's open transaction can legitimately hold the shared PostgreSQL snapshot horizon.
            await Task.Delay(20);
        }
        throw new InvalidOperationException("Committed fixture events did not pass the snapshot horizon.");
    }

    private static string Event(string site, int index) => $$"""
        <html lang="en"><script type="application/ld+json">{"@type":"Event","@id":"#event",
        "name":"The fixture {{index}}","description":"An event with music.","startDate":"2026-11-0{{index}}T19:00:00+05:00"}</script></html>
        """;
}
