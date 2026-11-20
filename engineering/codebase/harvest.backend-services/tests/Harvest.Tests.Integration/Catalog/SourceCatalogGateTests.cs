using System.Text;
using AwesomeAssertions;
using Harvest.Tests.Integration.Engine;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>The gates of an instance's source catalog over PostgreSQL: a source whose entry the instance turned off never
/// schedules, runs, captures or shows on the Sources page, and turning it on again resumes it as it was.</summary>
public sealed class SourceCatalogGateTests
{
    private static readonly ScheduleDeclaration Hourly = new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) };

    [Fact]
    public async Task OffSource_ShouldBeNoSchedulerCandidate_UntilItsEntryIsTurnedOnAgain()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync(schedules: [Hourly]);
        using var scope = host.Services.CreateScope();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();

        await CatalogSql.TurnOffAsync(host.ConnectionString, source);
        (await engine.QueueDueAsync()).Should().Be(0, "an off source is never a candidate");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs")).Should().Be(0);

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        (await engine.QueueDueAsync()).Should().Be(1, "turning on resumes the schedule as it was");
    }

    [Fact]
    public async Task OffSource_ShouldRefuseManualRunsAndResume_WithTheTurnedOffReason()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        await CatalogSql.TurnOffAsync(host.ConnectionString, await SourceOfAsync(scope, registration.PipelineId));

        var started = await runs.StartAsync(registration.PipelineId, WorkKind.Discover);
        EngineHost.Data(await runs.SetPausedAsync(registration.PipelineId, true)).State.Should().Be(PipelineState.Paused, "a pause is never refused");
        var resumed = await runs.SetPausedAsync(registration.PipelineId, false);

        foreach (var refusal in new[] { started.Should().BeOfType<Result<RunModel>.Failure>().Subject.Error, resumed.Should().BeOfType<Result<PipelineModel>.Failure>().Subject.Error })
        {
            refusal.Type.Should().Be(AppErrorType.Forbidden);
            refusal.Message.Should().StartWith(SourceCatalogConstants.TurnedOffReason);
        }
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs")).Should().Be(0);
        EngineHost.Data(await scope.ServiceProvider.GetRequiredService<IRegistrationBroker>().GetPipelineAsync(registration.PipelineId))
            .State.Should().Be(PipelineState.Paused, "a refused resume changes nothing");
    }

    [Fact]
    public async Task OffSource_ShouldLeaveQueuedWorkQueuedAndUnclaimed_UntilItsEntryIsTurnedOnAgain()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync();
        host.Fetcher.Set("https://mstar.uz/sitemap.xml", "<urlset><url><loc>https://mstar.uz/events/1</loc></url></urlset>", "application/xml");
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        await CatalogSql.TurnOffAsync(host.ConnectionString, await SourceOfAsync(scope, registration.PipelineId));

        (await host.ProcessAsync()).Should().BeFalse("the claim leaves an off source's work alone");

        host.Fetcher.Calls.Should().BeEmpty();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item WHERE state='queued'")).Should().Be(1);
        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        (await host.ProcessAsync()).Should().BeTrue();
        host.Fetcher.Calls.Should().Equal("https://mstar.uz/sitemap.xml");
    }

    [Fact]
    public async Task OffSource_ShouldRefuseCaptureWrites_AndWriteNothing()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        EngineHost.Data(await posts.SaveCaptureAsync(registration.ContainerId, source, null, CaptureOf(host, "one"), EnvelopeOf("One"))).Created.Should().BeTrue();
        await CatalogSql.TurnOffAsync(host.ConnectionString, source);

        var refused = await posts.SaveCaptureAsync(registration.ContainerId, source, null, CaptureOf(host, "two"), EnvelopeOf("Two"));

        refused.Should().BeOfType<Result<CaptureSaveResult>.Failure>().Subject.Error.Message.Should().StartWith(SourceCatalogConstants.TurnedOffReason);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_posts")).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(1);
        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        EngineHost.Data(await posts.SaveCaptureAsync(registration.ContainerId, source, null, CaptureOf(host, "two"), EnvelopeOf("Two"))).Created.Should().BeTrue();
    }

    [Fact]
    public async Task OffSource_ShouldLeaveTheOverviewButStillAnswerByIdAndInTheList_WithTurnedOnFalse()
    {
        await using var host = await StartAsync();
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var overview = scope.ServiceProvider.GetRequiredService<ISourceOverviewBroker>();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        EngineHost.Data(await overview.ListOverviewAsync(registration.ContainerId)).Should().ContainSingle(item => item.SourceId == source && item.CatalogKey == null);
        EngineHost.Data(await registry.GetSourceAsync(source)).Should().Match<SourceModel>(model => model.TurnedOn && model.CatalogKey == null);

        await CatalogSql.TurnOffAsync(host.ConnectionString, source);

        EngineHost.Data(await overview.ListOverviewAsync(registration.ContainerId)).Should().BeEmpty("the Sources page lists turned-on sources only");
        EngineHost.Data(await registry.GetSourceAsync(source)).Should().Match<SourceModel>(model => !model.TurnedOn && model.CatalogKey == CatalogSql.Key);
        EngineHost.Data(await registry.ListSourcesAsync(registration.ContainerId)).Should().ContainSingle(model => model.Id == source && !model.TurnedOn);
        var sections = EngineHost.Data(await registry.ListPipelinesAsync(registration.ContainerId));
        sections.Should().HaveCount(1, "the pipelines stay");

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        var back = EngineHost.Data(await overview.ListOverviewAsync(registration.ContainerId)).Should().ContainSingle().Subject;
        back.CatalogKey.Should().Be(CatalogSql.Key);
    }

    [Fact]
    public async Task TurningAnEntryOffAndOn_ShouldResumeTheSourceAsItWas_AndDeleteNothing()
    {
        var catalog = new TestCatalog("test", TestCatalog.Entry(CatalogSql.Key));
        await using var host = await StartAsync(catalog);
        var registration = await host.RegisterAsync(schedules: [Hourly]);
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        EngineHost.Data(await registry.UpdateSourceAsync(source, new() { ModelProcessing = ModelProcessingMode.Manual }));
        EngineHost.Data(await runs.SetPausedAsync(registration.PipelineId, true));
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        EngineHost.Data(await posts.SaveCaptureAsync(registration.ContainerId, source, null, CaptureOf(host, "one"), EnvelopeOf("One")));
        EngineHost.Data(await runs.SetPausedAsync(registration.PipelineId, false));
        EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Discover));
        EngineHost.Data(await registry.UpdateSourceAsync(source, new() { Paused = true }));
        await host.ExecuteAsync("UPDATE harvest_sources SET catalog_key=@key WHERE id=@id", ("key", CatalogSql.Key), ("id", source));
        var before = await SnapshotAsync(host, registry, registration.PipelineId, source);
        var broker = scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>();

        var off = EngineHost.Data(await broker.SwitchAsync(CatalogSql.Key, false, "subject-1"));
        (await SnapshotAsync(host, registry, registration.PipelineId, source)).Should().Be(before, "turning off changes no row");
        var on = EngineHost.Data(await broker.SwitchAsync(CatalogSql.Key, true, "subject-2"));

        off.State.Should().Be(CatalogState.Off);
        on.State.Should().Be(CatalogState.On);
        catalog.SetUps.Should().BeEmpty("an entry that has sources sets nothing up when it is turned on again");
        (await SnapshotAsync(host, registry, registration.PipelineId, source)).Should().Be(before,
            "pause, schedules, sections, mode and every row count come back as they were");
        before.Mode.Should().Be(ModelProcessingMode.Manual);
        before.SourceState.Should().Be(SourceState.Paused);
        before.Counts.Should().Be("1,1,1,1,1", "one source, pipeline, post, run and schedule exist, and none was deleted");
    }

    private static async Task<Guid> SourceOfAsync(IServiceScope scope, Guid pipelineId) =>
        EngineHost.Data(await scope.ServiceProvider.GetRequiredService<IRegistrationBroker>().GetPipelineAsync(pipelineId)).SourceId;

    private static Task<EngineHost> StartAsync(params ISourceCatalog[] catalogs) => EngineHost.StartAsync(services =>
    {
        services.AddHarvestSources();
        foreach (var catalog in catalogs)
            services.AddSingleton(catalog);
    });

    private static async Task<Snapshot> SnapshotAsync(EngineHost host, IRegistrationBroker registry, Guid pipelineId, Guid sourceId)
    {
        var pipeline = EngineHost.Data(await registry.GetPipelineAsync(pipelineId));
        var source = EngineHost.Data(await registry.GetSourceAsync(sourceId));
        string counts = await host.ScalarAsync<string>("""
            SELECT (SELECT count(*) FROM harvest_sources) || ',' || (SELECT count(*) FROM harvest_pipelines) || ',' || (SELECT count(*) FROM harvest_posts)
                || ',' || (SELECT count(*) FROM harvest_runs) || ',' || (SELECT count(*) FROM harvest_schedules)
            """);
        return new Snapshot(source.ModelProcessing, source.State, pipeline.State, pipeline.PauseReason,
            string.Join(";", pipeline.Schedules.Select(schedule => $"{schedule.Kind}/{schedule.Enabled}/{schedule.Every}/{schedule.Mode}")), counts);
    }

    private sealed record Snapshot(ModelProcessingMode Mode, SourceState SourceState, PipelineState Pipeline, PipelinePauseReason? Reason,
        string Schedules, string Counts);

    private static Capture CaptureOf(EngineHost host, string key) => new()
    {
        ExternalKey = key, Url = $"https://mstar.uz/{key}", RequestUrl = $"https://mstar.uz/{key}", FinalUrl = $"https://mstar.uz/{key}",
        CapturedAtUtc = host.Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(key), ContentProjection = key, HttpStatus = 200, ContentType = "text/html",
    };

    private static PostEnvelope EnvelopeOf(string title) => new()
    {
        Kind = PostKind.Event, Title = title, Text = title, Language = new PostLanguage { Code = "en" },
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };
}
