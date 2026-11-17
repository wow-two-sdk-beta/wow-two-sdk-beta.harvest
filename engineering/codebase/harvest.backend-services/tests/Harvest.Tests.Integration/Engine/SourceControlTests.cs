using AwesomeAssertions;
using Harvest.Tests.Integration.Sources;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Verifies the source master pause and stage-aware console over the real registry, scheduler and queue.</summary>
public sealed class SourceControlTests
{
    [Fact]
    public async Task MasterPause_ShouldStopNewAndQueuedWork_WithoutChangingSectionChoices()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules:
            [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }]);
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        var pipeline = EngineHost.Data(await registry.GetPipelineAsync(registration.PipelineId));
        var other = EngineHost.Data(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = pipeline.ContainerId, SourceId = pipeline.SourceId,
            Scope = new() { Url = "https://mstar.uz/other" },
            Schedules = [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(2) }],
        }));
        other = EngineHost.Data(await runs.SetPausedAsync(other.Id, true));
        var queued = EngineHost.Data(await runs.StartAsync(pipeline.Id));

        EngineHost.Data(await registry.UpdateSourceAsync(pipeline.SourceId, new() { Paused = true })).State.Should().Be(SourceState.Paused);
        (await engine.QueueDueAsync()).Should().Be(0);
        (await host.ProcessAsync()).Should().BeFalse();
        var rejected = (await runs.StartAsync(pipeline.Id)).Should().BeOfType<Result<RunModel>.Failure>().Subject;
        rejected.Error.Type.Should().Be(AppErrorType.Forbidden);
        EngineHost.Data(await runs.GetAsync(queued.Id)).Status.Should().Be(RunStatus.Queued);
        EngineHost.Data(await registry.GetPipelineAsync(pipeline.Id)).Should().BeEquivalentTo(pipeline);
        EngineHost.Data(await registry.GetPipelineAsync(other.Id)).Should().BeEquivalentTo(other);

        EngineHost.Data(await registry.UpdateSourceAsync(pipeline.SourceId, new() { Paused = false })).State.Should().Be(SourceState.Active);
        EngineHost.Data(await runs.StartAsync(pipeline.Id)).Id.Should().Be(queued.Id);
        EngineHost.Data(await registry.GetPipelineAsync(other.Id)).Should().BeEquivalentTo(other);
        (await engine.QueueDueAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Overview_ShouldShowTheEarliestEligibleStage_AndNewestStageActivity()
    {
        await using var host = await SourceHost.StartAsync(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));
        var registry = host.Registry;
        var source = EngineHost.Data(await registry.RegisterSourceAsync(new()
        {
            ContainerId = host.ContainerId, Family = SourceFamily.Site, Handle = "stage.example.test",
            Coverage = SourceCoverage.Whole, Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).Id;
        var pipeline = EngineHost.Data(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = host.ContainerId, SourceId = source,
            Schedules =
            [
                new() { Kind = WorkKind.Discover, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(3), Stage = new() },
                new() { Kind = WorkKind.Harvest, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1), Stage = new() { BatchSize = 50 } },
                new() { Kind = WorkKind.Process, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(30), Stage = new() { BatchSize = 50, Disabled = true } },
            ],
        }));
        var now = host.Clock.GetUtcNow();
        await host.ExecuteAsync("UPDATE harvest_schedules SET next_due_at=@now + every_interval WHERE pipeline_id=@pipeline",
            ("now", now), ("pipeline", pipeline.Id));
        var overview = host.Overview;
        var before = EngineHost.Data(await overview.ListOverviewAsync(host.ContainerId)).Single();
        before.State.Should().Be(SourceHarvestState.Healthy);
        before.NextHarvestAt.Should().Be(now.AddHours(1));
        before.Sections.Single().Should().Match<SourceSectionModel>(section => section.HarvestKind == WorkKind.Harvest
            && section.NextWorkKind == WorkKind.Harvest && section.Harvesting);

        var run = EngineHost.Data(await host.Runs.StartAsync(pipeline.Id, WorkKind.Discover));
        var after = EngineHost.Data(await overview.ListOverviewAsync(host.ContainerId)).Single();
        after.LastHarvest.Should().Match<SourceHarvestModel>(last => last.RunId == run.Id && last.Kind == WorkKind.Discover);
        EngineHost.Data(await registry.UpdateSourceAsync(source, new() { Paused = true }));
        var paused = EngineHost.Data(await overview.ListOverviewAsync(host.ContainerId)).Single();
        paused.State.Should().Be(SourceHarvestState.Idle);
        paused.NextHarvestAt.Should().BeNull();
        paused.Sections.Single().HarvestKind.Should().BeNull();
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("retired")]
    public async Task MasterPause_ShouldNotOverrideSourceAvailability(string state)
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var source = EngineHost.Data(await registry.GetPipelineAsync(registration.PipelineId)).SourceId;
        await host.ExecuteAsync("UPDATE harvest_sources SET state=@state WHERE id=@id", ("state", state), ("id", source));

        var result = await registry.UpdateSourceAsync(source, new() { Paused = false });

        result.Should().BeOfType<Result<SourceModel>.Failure>().Subject.Error.Type.Should().Be(AppErrorType.Conflict);
        (await host.ScalarAsync<string>("SELECT state FROM harvest_sources WHERE id=@id", ("id", source))).Should().Be(state);
    }
}
