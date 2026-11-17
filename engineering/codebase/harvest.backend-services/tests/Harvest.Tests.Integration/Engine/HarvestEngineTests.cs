using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
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
    public async Task Discovery_ShouldReadThePublishedCatalogAndIgnoreLegacyOverrides()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules:
        [
            new() { Kind = WorkKind.Discover, Mode = ScheduleMode.Manual, Stage = new() },
        ]);
        await host.ExecuteAsync("""
            UPDATE harvest_schedules SET stage_options=stage_options || '{"catalogUrl":"https://mstar.uz/private"}'::jsonb
            WHERE pipeline_id=@id AND kind='discover'
            """, ("id", registration.PipelineId));
        Fixtures(host, "mstar.uz", count: 1);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        (await host.DrainAsync()).Should().Be(1);
        EngineHost.Data(await runs.GetAsync(run.Id)).Discovered.Should().Be(1);
        host.Fetcher.Calls.Should().Equal("https://mstar.uz/sitemap.xml");

        var custom = await host.RegisterAsync(pipelineScope: new() { Url = "https://mstar.uz/unpublished" });
        var unavailable = EngineHost.Data(await runs.StartAsync(custom.PipelineId, WorkKind.Discover));
        unavailable.FetchOutcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        (await host.DrainAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Harvest_ShouldKeepTheBatchBacklogWhenTheHostGoesOffline()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 2);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        (await host.DrainAsync()).Should().Be(1);
        foreach (var index in new[] { 1, 2 })
            host.Fetcher.Set($"https://mstar.uz/events/{index}", (_, _) => Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.NetworkError, Message = "Fixture DNS failure.",
                RetryAfter = TimeSpan.FromMinutes(5), Usage = new() { Requests = 1 },
            }));

        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Harvest));
        (await host.DrainAsync()).Should().Be(1);
        EngineHost.Data(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Partial);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries WHERE detail_state IN ('pending','failed')")).Should().Be(2);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_step_attempts WHERE outcome='network_error' AND retry_at IS NOT NULL")).Should().Be(1);
    }

    [Fact]
    public async Task StageSwitch_ShouldGateManualAndScheduledRunsOnASubscribedPipeline()
    {
        await using var host = await EngineHost.StartAsync();
        var cadence = new ScheduleDeclaration
        {
            Kind = WorkKind.Harvest, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(5),
            Stage = new() { BatchSize = 5 },
        };
        var registration = await host.RegisterAsync(schedules: [cadence]);
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var pipeline = EngineHost.Data(await registry.GetPipelineAsync(registration.PipelineId));
        var source = EngineHost.Data(await registry.GetSourceAsync(pipeline.SourceId));
        var stages = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();

        async Task Save(ScheduleDeclaration update)
            => EngineHost.Data(await stages.SaveAsync(pipeline, source, update, seedOnly: false, CancellationToken.None))
                .Should().BeTrue();

        await Save(cadence with { Stage = new() { BatchSize = 5, Disabled = true } });
        await Save(cadence with { Stage = new() { BatchSize = 7 } });
        var saved = EngineHost.Data(await registry.GetPipelineAsync(pipeline.Id)).Schedules.Single();
        var options = saved.Stage!;
        saved.Enabled.Should().BeTrue();
        options.BatchSize.Should().Be(7);
        options.Disabled.Should().BeTrue();
        (await runs.StartAsync(pipeline.Id, WorkKind.Harvest)).Should().BeOfType<Result<RunModel>.Failure>();
        (await engine.QueueDueAsync()).Should().Be(0);

        await Save(cadence with { Stage = new() { BatchSize = 7, Disabled = false } });
        EngineHost.Data(await runs.SetPausedAsync(pipeline.Id, true)).State.Should().Be(PipelineState.Paused);
        (await engine.QueueDueAsync()).Should().Be(0);
        EngineHost.Data(await runs.SetPausedAsync(pipeline.Id, false)).State.Should().Be(PipelineState.Active);
        (await engine.QueueDueAsync()).Should().Be(1);
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        EngineHost.Data(await delivery.GetSubscriptionAsync(registration.SubscriptionId)).PipelineId.Should().Be(pipeline.Id);
    }

    [Fact]
    public async Task StageSchedule_ShouldKeepTheChosenCapturePathAcrossSavesAndSeeds()
    {
        await using var host = await EngineHost.StartAsync();
        var capture = new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6) };
        var stage = capture with { Kind = WorkKind.Harvest, Stage = new() { BatchSize = 7 } };
        var registration = await host.RegisterAsync(schedules: [capture]);
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var pipeline = EngineHost.Data(await registry.GetPipelineAsync(registration.PipelineId));
        var source = EngineHost.Data(await registry.GetSourceAsync(pipeline.SourceId));
        var stages = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        Task<Result<bool>> Save(ScheduleDeclaration setting, bool seedOnly = false) =>
            stages.SaveAsync(pipeline, source, setting, seedOnly, CancellationToken.None);

        EngineHost.Data(await Save(stage, seedOnly: true)).Should().BeFalse();
        EngineHost.Data(await Save(stage, seedOnly: true)).Should().BeFalse();
        Assert.IsType<Result<bool>.Failure>(await Save(stage)).Error.Message
            .Should().Be("A section runs either capture or the discover and harvest stages.");
        EngineHost.Data(await Save(stage with { Stage = new() { BatchSize = 5, Disabled = true } })).Should().BeTrue();
        EngineHost.Data(await Save(stage)).Should().BeTrue("saving options at the same cadence keeps the stage disabled");
        var updated = EngineHost.Data(await registry.UpdatePipelineAsync(pipeline.Id, new()
        {
            Scope = pipeline.Scope, Schedules = [capture, stage with { Stage = null }],
        }));
        updated.Schedules.Single(row => row.Kind == WorkKind.Harvest).Stage!.Disabled.Should().BeTrue();

        EngineHost.Data(await registry.UpdatePipelineAsync(pipeline.Id, new()
        {
            Scope = pipeline.Scope, Schedules = [capture with { Enabled = false }, stage],
        }));
        EngineHost.Data(await Save(stage with { Stage = new() { BatchSize = 7, Disabled = false } })).Should().BeTrue();
        EngineHost.Data(await Save(stage with { Stage = new() { BatchSize = 2 } }, seedOnly: true)).Should().BeFalse();
        var saved = EngineHost.Data(await registry.GetPipelineAsync(pipeline.Id));
        saved.Schedules.Single(row => row.Kind == WorkKind.Harvest).Stage!.BatchSize.Should().Be(7);

        // A mixed row created before the policy cannot queue duplicate capture work.
        await host.ExecuteAsync("UPDATE harvest_schedules SET enabled=true WHERE pipeline_id=@pipeline AND kind='capture'",
            ("pipeline", pipeline.Id));
        (await scope.ServiceProvider.GetRequiredService<HarvestEngine>().QueueDueAsync()).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@pipeline AND kind='capture'",
            ("pipeline", pipeline.Id))).Should().Be(0);
    }

    [Fact]
    public async Task Audit_ShouldShowTheActiveFetchAndItsCompletedOperations()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const string url = "https://mstar.uz/sitemap.xml";
        host.Fetcher.Set(url, async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return new FetchResponse { Outcome = FetchOutcome.NetworkError, Usage = new() { Requests = 1 } };
        });
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var audit = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        var execution = host.ProcessAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var active = (await audit.AuditAsync(run.Id, 0, 10, CancellationToken.None)).Single().Operations.Single();
            active.Key.Should().Be("fetch");
            active.FinishedAtUtc.Should().BeNull();
        }
        finally
        {
            release.TrySetResult();
            await execution;
        }
        var failed = (await audit.AuditAsync(run.Id, 0, 10, CancellationToken.None)).Single().Operations.Single();
        failed.Result!.Status.Should().Be("failed");
        failed.FinishedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Audit_ShouldIdentifyInvalidSavedCaptureShapeAtDeserialization()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        Fixtures(host, "mstar.uz", count: 1);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var audit = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        EngineHost.Data(await runs.StartAsync(registration.PipelineId));
        await host.DrainAsync();
        await host.ExecuteAsync("UPDATE harvest_link_readings SET fields='[]'::jsonb");
        var run = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Process));
        (await host.DrainAsync()).Should().Be(1);
        var operations = (await audit.AuditAsync(run.Id, 0, 10, CancellationToken.None)).Single().Operations;
        operations.Select(step => step.Key).Should().Equal("load-capture", "deserialize-capture");
        operations.Select(step => step.Result!.Status).Should().Equal("completed", "failed");
    }

    [Fact]
    public async Task Stages_ShouldResumeExactPendingPagesAndDetailsAfterRestart()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxPages = 1 }, schedules:
        [
            new() { Kind = WorkKind.Discover, Mode = ScheduleMode.Manual, Stage = new() { BatchSize = 1 } },
            new() { Kind = WorkKind.Harvest, Mode = ScheduleMode.Manual, Stage = new() { BatchSize = 1 } },
            new() { Kind = WorkKind.Process, Mode = ScheduleMode.Manual, Stage = new() { BatchSize = 1 } },
        ]);
        const string root = "https://mstar.uz/sitemap.xml";
        const string next = "https://mstar.uz/next.xml";
        host.Fetcher.Set(root, $"<sitemapindex><sitemap><loc>{next}</loc></sitemap></sitemapindex>", "application/xml");
        host.Fetcher.Set(next, "<urlset><url><loc>https://mstar.uz/events/1</loc></url><url><loc>https://mstar.uz/events/2</loc></url></urlset>", "application/xml");
        host.Fetcher.Set("https://mstar.uz/events/1", Event("mstar.uz", 1));
        host.Fetcher.Set("https://mstar.uz/events/2", Event("mstar.uz", 2));

        async Task<RunModel> Run(WorkKind kind, bool drain = true)
        {
            host.Clock.Advance(TimeSpan.FromMinutes(1));
            using var scope = host.Services.CreateScope();
            var broker = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            var queued = EngineHost.Data(await broker.StartAsync(registration.PipelineId, kind));
            if (drain)
                await host.DrainAsync();
            return EngineHost.Data(await broker.GetAsync(queued.Id));
        }

        var first = await Run(WorkKind.Discover, drain: false);
        (await host.ProcessAsync()).Should().BeTrue();
        host.Fetcher.Calls.Should().Equal(root);
        await host.RestartAsync();

        (await host.DrainAsync()).Should().Be(1);
        using (var scope = host.Services.CreateScope())
        {
            var broker = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            var resumed = EngineHost.Data(await broker.GetAsync(first.Id));
            resumed.Status.Should().Be(RunStatus.Completed);
            resumed.Discovered.Should().Be(2);
        }
        host.Fetcher.Calls.Should().Equal(root, next);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(0);

        (await Run(WorkKind.Harvest)).Captured.Should().Be(1);
        string pending = await host.ScalarAsync<string>("SELECT url FROM harvest_discoveries WHERE detail_state='pending'");
        await host.RestartAsync();
        (await Run(WorkKind.Harvest)).Captured.Should().Be(1);
        host.Fetcher.Calls.Last().Should().Be(pending);
        host.Fetcher.Calls.Should().OnlyHaveUniqueItems();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_step_attempts WHERE outcome='ok'")).Should().Be(4);

        (await Run(WorkKind.Process)).Requests.Should().Be(0);
        (await Run(WorkKind.Process)).Captured.Should().Be(1);
        (await Run(WorkKind.Process)).CompletionReason.Should().Be(RunReason.NoItems);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_processing_results WHERE result IS NOT NULL")).Should().Be(2);
        host.Fetcher.Calls.Count.Should().Be(4);
    }

    [Fact]
    public async Task ListingPageLimit_ShouldCompleteDetailsFromAdmittedListings()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(limits: new() { MaxPages = 2 });
        host.Fetcher.Set("https://mstar.uz/sitemap.xml",
            "<sitemapindex><sitemap><loc>https://mstar.uz/first.xml</loc></sitemap>"
            + "<sitemap><loc>https://mstar.uz/excluded.xml</loc></sitemap></sitemapindex>", "application/xml");
        host.Fetcher.Set("https://mstar.uz/first.xml",
            "<urlset><url><loc>https://mstar.uz/events/1</loc></url><url><loc>https://mstar.uz/events/2</loc></url></urlset>",
            "application/xml");
        host.Fetcher.Set("https://mstar.uz/events/1", Event("mstar.uz", 1));
        host.Fetcher.Set("https://mstar.uz/events/2", Event("mstar.uz", 2));
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var queued = EngineHost.Data(await runs.StartAsync(registration.PipelineId));

        (await host.DrainAsync()).Should().Be(4);
        var run = EngineHost.Data(await runs.GetAsync(queued.Id));
        run.Status.Should().Be(RunStatus.Partial);
        run.CompletionReason.Should().Be(RunReason.PageLimit);
        run.NewPosts.Should().Be(2);
        host.Fetcher.Calls.Should().NotContain("https://mstar.uz/excluded.xml");
    }

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
