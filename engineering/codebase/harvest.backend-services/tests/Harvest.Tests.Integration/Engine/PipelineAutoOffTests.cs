using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using FetchResponse = WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching.FetchResponse;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Verifies the failed-run count and the automatic turn-off of a pipeline's harvesting schedules over the real engine.</summary>
public sealed class PipelineAutoOffTests
{
    private const string Root = "https://mstar.uz/sitemap.xml";

    /// <summary>Holds a step past the longest lane pause (16 hours) so the next run can claim its work.</summary>
    private static readonly TimeSpan PastThePauses = TimeSpan.FromHours(17);

    [Fact]
    public async Task TenBlockedRuns_ShouldTurnTheHarvestingSchedulesOff_AndLeaveTheProcessRowTheStateAndManualRuns()
    {
        var logs = new CapturedLogs();
        await using var host = await EngineHost.StartAsync(services => services.AddSingleton<ILoggerProvider>(logs));
        var registration = await host.RegisterAsync(schedules:
            [Interval(WorkKind.Capture), Interval(WorkKind.Check, 24), Manual(WorkKind.Refresh), Interval(WorkKind.Process)]);
        await host.ExecuteAsync("UPDATE harvest_schedules SET next_due_at=@due", ("due", host.Clock.GetUtcNow().AddDays(1)));
        Block(host);

        RunModel tenth = null!;
        for (var index = 1; index <= 10; index++)
        {
            tenth = await RunAsync(host, registration.PipelineId);
            tenth.Status.Should().Be(RunStatus.Partial);
            tenth.FetchOutcome.Should().Be(FetchOutcome.Blocked);
            if (index < 10)
            {
                (await StateAsync(host, registration.PipelineId)).Should().Be(new PipelineAutoOffState(10, true, index, null));
                (await EnabledAsync(host, registration.PipelineId, "capture")).Should().BeTrue();
            }
        }

        (await EnabledAsync(host, registration.PipelineId, "capture")).Should().BeFalse();
        (await EnabledAsync(host, registration.PipelineId, "check")).Should().BeFalse();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_schedules WHERE pipeline_id=@p AND next_due_at IS NOT NULL AND kind IN ('capture','check')",
            ("p", registration.PipelineId))).Should().Be(0);
        (await EnabledAsync(host, registration.PipelineId, "refresh")).Should().BeTrue("a manual row stays");
        (await EnabledAsync(host, registration.PipelineId, "process")).Should().BeTrue("a process row is never turned off");
        (await host.ScalarAsync<string>("SELECT state FROM harvest_pipelines WHERE id=@p", ("p", registration.PipelineId))).Should().Be("active");
        var pipeline = (await Registry(host).GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        pipeline.AutoOff.Should().NotBeNull();
        pipeline.AutoOff!.AtUtc.Should().Be(host.Clock.GetUtcNow());
        pipeline.AutoOff.RunId.Should().Be(tenth.Id);
        pipeline.AutoOff.Failures.Should().Be(10);
        pipeline.AutoOff.Reason.Should().Be("blocked");
        (await StateAsync(host, registration.PipelineId)).Should().Be(new PipelineAutoOffState(10, true, 10, pipeline.AutoOff));
        var warning = logs.Warnings.Should().ContainSingle().Which;
        warning.Should().Contain(registration.PipelineId.ToString()).And.Contain("blocked").And.NotContain("http");

        (await RunAsync(host, registration.PipelineId)).Status.Should().Be(RunStatus.Partial, "a run started by hand still runs");
    }

    [Fact]
    public async Task NineBlockedRuns_ShouldNotTurnThePipelineOff_WhenARunThatCapturesComesBetweenThem()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Capture)]);
        Block(host);
        for (var index = 0; index < 9; index++)
            await RunAsync(host, registration.PipelineId);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(9);

        Fixtures(host);
        var captured = await RunAsync(host, registration.PipelineId);
        captured.Status.Should().Be(RunStatus.Completed);
        captured.Captured.Should().Be(1);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(0);

        Block(host);
        for (var index = 0; index < 9; index++)
            await RunAsync(host, registration.PipelineId);
        var state = await StateAsync(host, registration.PipelineId);
        state.Failures.Should().Be(9);
        state.Off.Should().BeNull();
        (await EnabledAsync(host, registration.PipelineId, "capture")).Should().BeTrue();
    }

    [Fact]
    public async Task NetworkErrorAndChallengeRuns_ShouldCount_WhileACancelledRunAndANoItemsRunNeitherCountNorReset()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Discover), Manual(WorkKind.Harvest)]);

        Block(host, FetchOutcome.NetworkError);
        (await RunAsync(host, registration.PipelineId, WorkKind.Discover)).FetchOutcome.Should().Be(FetchOutcome.NetworkError);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(1);
        Block(host, FetchOutcome.Challenge);
        (await RunAsync(host, registration.PipelineId, WorkKind.Discover)).FetchOutcome.Should().Be(FetchOutcome.Challenge);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(2);

        using (var scope = host.Services.CreateScope())
        {
            var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            var queued = EngineHost.Data(await runs.StartAsync(registration.PipelineId, WorkKind.Capture));
            EngineHost.Data(await runs.CancelAsync(queued.Id)).Status.Should().Be(RunStatus.Cancelled);
        }
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(2, "a cancelled run says nothing");

        var empty = await RunAsync(host, registration.PipelineId, WorkKind.Harvest);
        empty.Status.Should().Be(RunStatus.Completed);
        empty.CompletionReason.Should().Be(RunReason.NoItems);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(2, "a run with nothing to do neither counts nor resets");

        Block(host);
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(3);
    }

    [Fact]
    public async Task ADiscoverRunThatSavedDiscoveries_ShouldStartTheCountAgain_EvenWhenItEndedFailed()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Discover)]);
        Block(host);
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(2);

        Fixtures(host);
        var found = await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        found.Discovered.Should().Be(1);
        await host.ExecuteAsync("UPDATE harvest_runs SET status='failed',completion_reason='detail_failures' WHERE id=@run", ("run", found.Id));
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(0);

        Block(host);
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(1);
    }

    [Fact]
    public async Task APipelinesOwnCount_ShouldWinOverTheHostsAndZeroShouldNeverTurnItOff()
    {
        await using var host = await EngineHost.StartAsync();
        var own = await host.RegisterAsync(schedules: [Interval(WorkKind.Capture)]);
        var never = await host.RegisterAsync(host: "never.example", schedules: [Interval(WorkKind.Capture)]);
        using (var scope = host.Services.CreateScope())
        {
            var autoOff = scope.ServiceProvider.GetRequiredService<PipelineAutoOff>();
            (await autoOff.SetAfterAsync(own.PipelineId, 3)).ValueOrThrow().Should().Be(new PipelineAutoOffState(3, false, 0, null));
            (await autoOff.SetAfterAsync(never.PipelineId, 0)).ValueOrThrow().Should().Be(new PipelineAutoOffState(0, false, 0, null));
        }
        Block(host);
        host.Fetcher.Set("https://never.example/sitemap.xml", (_, _) => Task.FromResult(new FetchResponse { Outcome = FetchOutcome.Blocked }));

        for (var index = 1; index <= 3; index++)
            await RunAsync(host, own.PipelineId);
        var off = (await Registry(host).GetPipelineAsync(own.PipelineId)).ValueOrThrow().AutoOff;
        off.Should().NotBeNull();
        off!.Failures.Should().Be(3);
        (await EnabledAsync(host, own.PipelineId, "capture")).Should().BeFalse();

        for (var index = 1; index <= 15; index++)
            await RunAsync(host, never.PipelineId);
        var state = await StateAsync(host, never.PipelineId);
        state.Should().Be(new PipelineAutoOffState(0, false, 15, null));
        (await EnabledAsync(host, never.PipelineId, "capture")).Should().BeTrue();

        using var after = host.Services.CreateScope();
        var service = after.ServiceProvider.GetRequiredService<PipelineAutoOff>();
        (await service.SetAfterAsync(own.PipelineId, null)).ValueOrThrow().Should().Match<PipelineAutoOffState>(value => value.After == 10 && value.FromDefault);
        (await service.SetAfterAsync(own.PipelineId, 1001)).Should().BeOfType<Result<PipelineAutoOffState>.Failure>();
        (await service.SetAfterAsync(own.PipelineId, -1)).Should().BeOfType<Result<PipelineAutoOffState>.Failure>();
        (await service.SetAfterAsync(Guid.NewGuid(), 5)).Should().BeOfType<Result<PipelineAutoOffState>.Failure>();
        (await service.ReadAsync(Guid.NewGuid())).Should().BeOfType<Result<PipelineAutoOffState>.Failure>();
        (await EnabledAsync(host, own.PipelineId, "capture")).Should().BeFalse("setting the count never turns a row on");
    }

    [Fact]
    public async Task TheHostsCount_ShouldApplyToAPipelineWithNoValueOfItsOwn()
    {
        await using var host = await EngineHost.StartAsync(services => services.Configure<HarvestEngineOptions>(options => options.AutoOffAfter = 2));
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Capture)]);
        Block(host);

        await RunAsync(host, registration.PipelineId);
        (await StateAsync(host, registration.PipelineId)).Should().Be(new PipelineAutoOffState(2, true, 1, null));
        await RunAsync(host, registration.PipelineId);

        var state = await StateAsync(host, registration.PipelineId);
        state.Off.Should().NotBeNull();
        state.Off!.Failures.Should().Be(2);
        (await EnabledAsync(host, registration.PipelineId, "capture")).Should().BeFalse();
    }

    [Fact]
    public async Task ARowThatFailsAtQueueTimeOnEverySlot_ShouldTurnTheRowsOffThroughTheScheduler()
    {
        var logs = new CapturedLogs();
        await using var host = await EngineHost.StartAsync(services => services.AddSingleton<ILoggerProvider>(logs));
        var registration = await host.RegisterAsync(pipelineScope: new() { Url = "https://mstar.uz/unpublished" },
            schedules: [Interval(WorkKind.Discover), Interval(WorkKind.Harvest)]);
        using (var scope = host.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PipelineAutoOff>().SetAfterAsync(registration.PipelineId, 3)).ValueOrThrow();

        for (var slot = 1; slot <= 3; slot++)
        {
            using var scope = host.Services.CreateScope();
            (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(slot - 1);
            await scope.ServiceProvider.GetRequiredService<HarvestEngine>().QueueDueAsync();
            host.Clock.Advance(TimeSpan.FromHours(2));
        }

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@p AND kind='discover' AND status='failed' AND fetch_outcome='capability_unavailable'",
            ("p", registration.PipelineId))).Should().Be(3);
        (await EnabledAsync(host, registration.PipelineId, "discover")).Should().BeFalse();
        (await EnabledAsync(host, registration.PipelineId, "harvest")).Should().BeFalse();
        var state = await StateAsync(host, registration.PipelineId);
        state.Off.Should().NotBeNull();
        state.Off!.Failures.Should().Be(3);
        state.Off.Reason.Should().Be("capability_unavailable");
        logs.Warnings.Should().ContainSingle();

        var runCount = await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@p", ("p", registration.PipelineId));
        using var after = host.Services.CreateScope();
        (await after.ServiceProvider.GetRequiredService<HarvestEngine>().QueueDueAsync()).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_runs WHERE pipeline_id=@p", ("p", registration.PipelineId)))
            .Should().Be(runCount, "a turned-off pipeline queues nothing more");
    }

    [Fact]
    public async Task TurningARowBackOn_ShouldClearTheRecordAndTheCount_AndRunsQueuedBeforeItNeverCountAgain()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Capture), Interval(WorkKind.Check, 24)]);
        using (var scope = host.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PipelineAutoOff>().SetAfterAsync(registration.PipelineId, 3)).ValueOrThrow();
        Block(host);

        for (var index = 0; index < 2; index++)
            await RunAsync(host, registration.PipelineId);
        var pipeline = (await Registry(host).GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var unchanged = new PipelineUpdate { Name = pipeline.Name, Scope = pipeline.Scope, Schedules = [Interval(WorkKind.Capture), Interval(WorkKind.Check, 24)] };
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        EngineHost.Data(await Registry(host).UpdatePipelineAsync(registration.PipelineId, unchanged));
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(2, "saving live rows as they were resets nothing");

        var third = await RunAsync(host, registration.PipelineId);
        (await StateAsync(host, registration.PipelineId)).Off!.RunId.Should().Be(third.Id);
        (await EnabledAsync(host, registration.PipelineId, "capture")).Should().BeFalse();

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var resumed = EngineHost.Data(await Registry(host).UpdatePipelineAsync(registration.PipelineId, unchanged));
        resumed.AutoOff.Should().BeNull();
        resumed.Schedules.Should().OnlyContain(row => row.Enabled);
        (await StateAsync(host, registration.PipelineId)).Should().Be(new PipelineAutoOffState(3, false, 0, null));

        await RunAsync(host, registration.PipelineId);
        await RunAsync(host, registration.PipelineId);
        (await StateAsync(host, registration.PipelineId)).Should().Be(new PipelineAutoOffState(3, false, 2, null),
            "the three runs before the save never count again");
        var again = await RunAsync(host, registration.PipelineId);
        (await StateAsync(host, registration.PipelineId)).Off!.RunId.Should().Be(again.Id);
    }

    [Fact]
    public async Task SavingAStageRow_ShouldClearTheRecordWhenItTurnsTheRowBackOn_AndAStartupSeedShouldLeaveATurnedOffPipelineAlone()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Discover), Manual(WorkKind.Harvest)]);
        using (var scope = host.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PipelineAutoOff>().SetAfterAsync(registration.PipelineId, 2)).ValueOrThrow();
        Block(host);

        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        using var services = host.Services.CreateScope();
        var stages = services.ServiceProvider.GetRequiredService<PipelineStages>();
        var pipeline = (await Registry(host).GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await Registry(host).GetSourceAsync(pipeline.SourceId)).ValueOrThrow();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        (await stages.SaveAsync(pipeline, source, Stage(WorkKind.Discover), seedOnly: false, CancellationToken.None)).ValueOrThrow();
        (await StateAsync(host, registration.PipelineId)).Failures.Should().Be(1, "saving a live row as it was resets nothing");

        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        pipeline = (await Registry(host).GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        pipeline.AutoOff.Should().NotBeNull();
        (await EnabledAsync(host, registration.PipelineId, "discover")).Should().BeFalse();

        (await stages.SaveAsync(pipeline, source, Stage(WorkKind.Discover), seedOnly: true, CancellationToken.None)).ValueOrThrow()
            .Should().BeFalse("a startup seed never touches a turned-off pipeline");
        (await EnabledAsync(host, registration.PipelineId, "discover")).Should().BeFalse();
        (await StateAsync(host, registration.PipelineId)).Off.Should().NotBeNull();

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        (await stages.SaveAsync(pipeline, source, Stage(WorkKind.Discover), seedOnly: false, CancellationToken.None)).ValueOrThrow();
        (await EnabledAsync(host, registration.PipelineId, "discover")).Should().BeTrue();
        (await StateAsync(host, registration.PipelineId)).Should().Be(new PipelineAutoOffState(2, false, 0, null));
        (await Registry(host).GetPipelineAsync(registration.PipelineId)).ValueOrThrow().AutoOff.Should().BeNull();
    }

    [Fact]
    public async Task AFailedManualRunOnAPipelineThatIsAlreadyOff_ShouldWriteNoSecondRecord()
    {
        var logs = new CapturedLogs();
        await using var host = await EngineHost.StartAsync(services => services.AddSingleton<ILoggerProvider>(logs));
        var registration = await host.RegisterAsync(schedules: [Interval(WorkKind.Capture)]);
        using (var scope = host.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<PipelineAutoOff>().SetAfterAsync(registration.PipelineId, 2)).ValueOrThrow();
        Block(host);
        await RunAsync(host, registration.PipelineId);
        var second = await RunAsync(host, registration.PipelineId);
        var record = (await StateAsync(host, registration.PipelineId)).Off;
        record.Should().NotBeNull();
        record!.RunId.Should().Be(second.Id);

        var manual = await RunAsync(host, registration.PipelineId);
        manual.Status.Should().Be(RunStatus.Partial);
        (await StateAsync(host, registration.PipelineId)).Off.Should().Be(record);
        logs.Warnings.Should().ContainSingle();
    }

    private static ScheduleDeclaration Interval(WorkKind kind, int hours = 1) => new()
    {
        Kind = kind, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(hours),
        Stage = kind is WorkKind.Discover or WorkKind.Harvest or WorkKind.Process ? new() { BatchSize = 1 } : null,
    };

    private static ScheduleDeclaration Manual(WorkKind kind) => new()
    {
        Kind = kind, Mode = ScheduleMode.Manual,
        Stage = kind is WorkKind.Discover or WorkKind.Harvest or WorkKind.Process ? new() { BatchSize = 1 } : null,
    };

    private static ScheduleDeclaration Stage(WorkKind kind) => Interval(kind);

    private static IRegistrationBroker Registry(EngineHost host) =>
        host.Services.CreateScope().ServiceProvider.GetRequiredService<IRegistrationBroker>();

    /// <summary>Makes the site's root page answer with a refusal or an error, and no capture.</summary>
    private static void Block(EngineHost host, FetchOutcome outcome = FetchOutcome.Blocked) =>
        host.Fetcher.Set(Root, (_, _) => Task.FromResult(new FetchResponse
        {
            Outcome = outcome, Usage = new() { Requests = 1 },
            Challenge = outcome == FetchOutcome.Challenge ? ChallengeKind.CaptchaTurnstile : null,
        }));

    private static void Fixtures(EngineHost host)
    {
        host.Fetcher.Set(Root, "<urlset><url><loc>https://mstar.uz/events/1</loc></url></urlset>", "application/xml");
        host.Fetcher.Set("https://mstar.uz/events/1", """
            <html lang="en"><script type="application/ld+json">{"@type":"Event","@id":"#event",
            "name":"The fixture 1","description":"An event with music.","startDate":"2026-11-01T19:00:00+05:00"}</script></html>
            """);
    }

    /// <summary>Starts one run by hand after the lane's and the host's pauses, and works it off.</summary>
    private static async Task<RunModel> RunAsync(EngineHost host, Guid pipeline, WorkKind kind = WorkKind.Capture)
    {
        host.Clock.Advance(PastThePauses);
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var started = EngineHost.Data(await runs.StartAsync(pipeline, kind));
        await host.DrainAsync();
        return EngineHost.Data(await runs.GetAsync(started.Id));
    }

    private static async Task<PipelineAutoOffState> StateAsync(EngineHost host, Guid pipeline)
    {
        using var scope = host.Services.CreateScope();
        return EngineHost.Data(await scope.ServiceProvider.GetRequiredService<PipelineAutoOff>().ReadAsync(pipeline));
    }

    private static Task<bool> EnabledAsync(EngineHost host, Guid pipeline, string kind) =>
        host.ScalarAsync<bool>("SELECT enabled FROM harvest_schedules WHERE pipeline_id=@p AND kind=@k", ("p", pipeline), ("k", kind));

    /// <summary>Keeps the warnings the auto-off logs, to check what they name.</summary>
    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> warnings = [];

        internal IReadOnlyList<string> Warnings => [.. warnings];

        public ILogger CreateLogger(string categoryName) =>
            categoryName.EndsWith(nameof(PipelineAutoOff), StringComparison.Ordinal) ? new Sink(warnings) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class Sink(ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
