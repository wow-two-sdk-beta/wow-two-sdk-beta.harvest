using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Exercises model processing admission and frozen prompt versions without a provider call.</summary>
public sealed class ProcessingAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledAutomaticStage_ShouldRemainEditableWithoutAiReadiness(bool aiAllowed)
    {
        var processor = new RecordingProcessor(unavailablePromptVersion: 9);
        await using var host = await EngineHost.StartAsync(services => services.AddKeyedSingleton<ICaptureProcessor>(processor.Key, processor));
        var registration = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var pipeline = (await registry.GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await registry.UpdateSourceAsync(pipeline.SourceId, new SourceUpdate { AiAllowed = aiAllowed })).ValueOrThrow();
        var stages = services.GetRequiredService<PipelineStages>();
        ScheduleDeclaration Settings(bool? disabled, int batch) => new()
        {
            Kind = WorkKind.Process, Enabled = true, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1),
            Stage = new() { BatchSize = batch, Processor = processor.Key, Model = "fixture/model", PromptVersion = 9, Disabled = disabled },
        };
        (await stages.SaveAsync(pipeline, source, Settings(true, 1), false, CancellationToken.None)).ValueOrThrow().Should().BeTrue();
        (await stages.SaveAsync(pipeline, source, Settings(null, 2), false, CancellationToken.None)).ValueOrThrow().Should().BeTrue();
        var saved = (await registry.GetPipelineAsync(pipeline.Id)).ValueOrThrow().Schedules.Single(schedule => schedule.Kind == WorkKind.Process);
        saved.Stage!.Disabled.Should().BeTrue();
        saved.Stage.BatchSize.Should().Be(2);
        (await stages.SaveAsync(pipeline, source, Settings(false, 2), false, CancellationToken.None))
            .Should().BeOfType<Result<bool>.Failure>();
        processor.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingPrompt_ShouldRefuseEveryAutomaticConfigurationPathAndReportReadiness()
    {
        var processor = new RecordingProcessor(unavailablePromptVersion: 9);
        await using var host = await EngineHost.StartAsync(services => services.AddKeyedSingleton<ICaptureProcessor>(processor.Key, processor));
        var registration = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var pipeline = (await registry.GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await registry.UpdateSourceAsync(pipeline.SourceId, new SourceUpdate { AiAllowed = true })).ValueOrThrow();
        var schedule = new ScheduleDeclaration
        {
            Kind = WorkKind.Process, Enabled = true, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1),
            Stage = new() { BatchSize = 1, Processor = processor.Key, Model = "fixture/model", PromptVersion = 9 },
        };
        var declared = await registry.DeclarePipelineAsync(new()
        {
            ContainerId = pipeline.ContainerId, SourceId = source.Id, Schedules = [schedule],
        });
        declared.Should().BeOfType<Result<PipelineModel>.Failure>().Which.Error.Message.Should().Be("The pinned prompt is unavailable.");
        var updated = await registry.UpdatePipelineAsync(pipeline.Id, new() { Scope = pipeline.Scope, Schedules = [schedule] });
        updated.Should().BeOfType<Result<PipelineModel>.Failure>().Which.Error.Message.Should().Be("The pinned prompt is unavailable.");
        var stages = services.GetRequiredService<PipelineStages>();
        var staged = await stages.SaveAsync(pipeline, source, schedule, false, CancellationToken.None);
        staged.Should().BeOfType<Result<bool>.Failure>().Which.Error.Message.Should().Be("The pinned prompt is unavailable.");

        await SetPromptAsync(services, pipeline, source, processor.Key, 9);
        pipeline = (await registry.GetPipelineAsync(pipeline.Id)).ValueOrThrow();
        (await stages.ListAsync(pipeline, source, CancellationToken.None)).Single(stage => stage.Kind == WorkKind.Process)
            .ReadinessError.Should().Be("The pinned prompt is unavailable.");
        (await services.GetRequiredService<IRunBroker>().StartAsync(pipeline.Id, WorkKind.Process))
            .Should().BeOfType<Result<RunModel>.Failure>();
        processor.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SourceOptOut_ShouldRefuseNewWorkAndStopAnAlreadyQueuedCall()
    {
        var processor = new RecordingProcessor();
        await using var host = await EngineHost.StartAsync(services => services.AddKeyedSingleton<ICaptureProcessor>(processor.Key, processor));
        var registration = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var runs = services.GetRequiredService<IRunBroker>();
        var pipeline = (await registry.GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await registry.GetSourceAsync(pipeline.SourceId)).ValueOrThrow();
        var capture = await SaveReadingAsync(services, pipeline, source, host.Clock.GetUtcNow());
        await SetPromptAsync(services, pipeline, source, processor.Key, 1);

        (await registry.UpdateSourceAsync(source.Id, new SourceUpdate { AiAllowed = false })).ValueOrThrow().AiAllowed.Should().BeFalse();
        var refused = await runs.StartAsync(pipeline.Id, WorkKind.Process);
        refused.Should().BeOfType<Result<RunModel>.Failure>().Which.Error.Message.Should().Contain("disabled for this source");
        (await runs.ListAsync(pipeline.Id)).ValueOrThrow().Should().BeEmpty();

        (await registry.UpdateSourceAsync(source.Id, new SourceUpdate { AiAllowed = true })).ValueOrThrow().AiAllowed.Should().BeTrue();
        var queued = (await runs.StartAsync(pipeline.Id, WorkKind.Process)).ValueOrThrow();
        queued.Status.Should().Be(RunStatus.Queued);
        (await registry.UpdateSourceAsync(source.Id, new SourceUpdate { AiAllowed = false })).ValueOrThrow().AiAllowed.Should().BeFalse();

        (await host.DrainAsync()).Should().Be(1);
        processor.Calls.Should().BeEmpty();
        var results = (await services.GetRequiredService<IPostProcessingBroker>()
            .ListAsync(pipeline.ContainerId, capture.Post.Id)).ValueOrThrow();
        results.Should().ContainSingle().Which.Output.Should().BeNull();
    }

    [Fact]
    public async Task PromptVersion_ShouldStayFrozenAndReprocessTheSameCaptureOnlyWhenItsPinChanges()
    {
        var processor = new RecordingProcessor();
        await using var host = await EngineHost.StartAsync(services => services.AddKeyedSingleton<ICaptureProcessor>(processor.Key, processor));
        var registration = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var runs = services.GetRequiredService<IRunBroker>();
        var pipeline = (await registry.GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await registry.GetSourceAsync(pipeline.SourceId)).ValueOrThrow();
        var capture = await SaveReadingAsync(services, pipeline, source, host.Clock.GetUtcNow());
        var captureId = capture.CaptureId!.Value;
        await SetPromptAsync(services, pipeline, source, processor.Key, 1);
        (await runs.StartAsync(pipeline.Id, WorkKind.Process)).ValueOrThrow().Status.Should().Be(RunStatus.Queued);
        await SetPromptAsync(services, pipeline, source, processor.Key, 2);

        (await host.DrainAsync()).Should().Be(1);
        processor.Calls.Should().Equal((captureId, (int?)1));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        (await runs.StartAsync(pipeline.Id, WorkKind.Process)).ValueOrThrow().Status.Should().Be(RunStatus.Queued);
        (await host.DrainAsync()).Should().Be(1);
        processor.Calls.Should().Equal((captureId, (int?)1), (captureId, (int?)2));

        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var repeat = (await runs.StartAsync(pipeline.Id, WorkKind.Process)).ValueOrThrow();
        repeat.CompletionReason.Should().Be(RunReason.NoItems);
        (await host.DrainAsync()).Should().Be(0);
        processor.Calls.Should().HaveCount(2);
        var results = (await services.GetRequiredService<IPostProcessingBroker>()
            .ListAsync(pipeline.ContainerId, capture.Post.Id)).ValueOrThrow();
        results.Should().HaveCount(2).And.OnlyContain(result => result.CaptureId == captureId && result.IsCurrent);
        results.Select(result => result.Output!.Value.GetProperty("promptVersion").GetInt32()).Should().Equal(2, 1);
    }

    [Fact]
    public async Task TerminalFailure_ShouldRequireReviewAndSkipLaterSchedulesForTheSamePin()
    {
        var processor = new RecordingProcessor(rejectedPromptVersion: 1);
        await using var host = await EngineHost.StartAsync(services => services.AddKeyedSingleton<ICaptureProcessor>(processor.Key, processor));
        var registration = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var engine = services.GetRequiredService<HarvestEngine>();
        var broker = services.GetRequiredService<IPostProcessingBroker>();
        var pipeline = (await registry.GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await registry.GetSourceAsync(pipeline.SourceId)).ValueOrThrow();
        var capture = await SaveReadingAsync(services, pipeline, source, host.Clock.GetUtcNow());
        var captureId = capture.CaptureId!.Value;
        var every = TimeSpan.FromHours(1);
        await SetPromptAsync(services, pipeline, source, processor.Key, 1, every);

        (await engine.QueueDueAsync()).Should().Be(1);
        (await host.DrainAsync()).Should().Be(1);
        var failed = (await broker.ListAsync(pipeline.ContainerId, capture.Post.Id)).ValueOrThrow().Single();
        failed.RequiresReview.Should().BeTrue();
        failed.Output.Should().BeNull();

        host.Clock.Advance(TimeSpan.FromDays(1));
        (await engine.QueueDueAsync()).Should().Be(1);
        (await host.DrainAsync()).Should().Be(0);
        processor.Calls.Should().Equal((captureId, (int?)1));

        await SetPromptAsync(services, pipeline, source, processor.Key, 2, every);
        host.Clock.Advance(every);
        (await engine.QueueDueAsync()).Should().Be(1);
        (await host.DrainAsync()).Should().Be(1);
        processor.Calls.Should().Equal((captureId, (int?)1), (captureId, (int?)2));
        var history = (await broker.ListAsync(pipeline.ContainerId, capture.Post.Id)).ValueOrThrow();
        history.Select(item => item.RequiresReview).Should().Equal(false, true);
        history[0].Output!.Value.GetProperty("promptVersion").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Purge_ShouldPreventAnInFlightResultFromBeingSaved()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new RecordingProcessor(beforeAnswer: async ct =>
        {
            entered.TrySetResult();
            await resume.Task.WaitAsync(ct);
        });
        await using var host = await EngineHost.StartAsync(services => services.AddKeyedSingleton<ICaptureProcessor>(processor.Key, processor));
        var registration = await host.RegisterAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var pipeline = (await registry.GetPipelineAsync(registration.PipelineId)).ValueOrThrow();
        var source = (await registry.GetSourceAsync(pipeline.SourceId)).ValueOrThrow();
        var capture = await SaveReadingAsync(services, pipeline, source, host.Clock.GetUtcNow());
        await SetPromptAsync(services, pipeline, source, processor.Key, 1);
        var runs = services.GetRequiredService<IRunBroker>();
        var queued = (await runs.StartAsync(pipeline.Id, WorkKind.Process)).ValueOrThrow();

        var execution = host.ProcessAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await host.ExecuteAsync("UPDATE harvest_posts SET purged_at=@now WHERE id=@post",
                ("now", host.Clock.GetUtcNow()), ("post", capture.Post.Id));
        }
        finally
        {
            resume.TrySetResult();
        }

        (await execution).Should().BeTrue();
        (await runs.GetAsync(queued.Id)).ValueOrThrow().Captured.Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_processing_results WHERE capture_id=@capture AND result IS NOT NULL",
            ("capture", capture.CaptureId!.Value))).Should().Be(0);
    }

    private static async Task SetPromptAsync(IServiceProvider services, PipelineModel pipeline, SourceModel source,
        string processor, int version, TimeSpan? every = null)
    {
        var stage = new ScheduleDeclaration
        {
            Kind = WorkKind.Process, Mode = every is null ? ScheduleMode.Manual : ScheduleMode.Interval, Every = every,
            Stage = new PipelineStageOptions { BatchSize = 5, Processor = processor, Model = "fixture/model", PromptVersion = version },
        };
        (await services.GetRequiredService<PipelineStages>().SaveAsync(pipeline, source, stage, seedOnly: false, CancellationToken.None))
            .ValueOrThrow().Should().BeTrue();
    }

    private static async Task<CaptureSaveResult> SaveReadingAsync(IServiceProvider services, PipelineModel pipeline, SourceModel source,
        DateTimeOffset capturedAt)
    {
        var url = $"https://{source.Handle}/listing";
        var capture = new Capture
        {
            ExternalKey = url, Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = capturedAt,
            Body = Encoding.UTF8.GetBytes("Deposit required"), ContentProjection = "Deposit required",
        };
        var envelope = new PostEnvelope
        {
            Title = "Rental listing", Text = "Deposit required",
            Extraction = new ExtractionStamp { NormVersion = "fixture@1", RulesVersion = "fixture@1" },
        };
        return (await services.GetRequiredService<IPostBroker>()
            .SaveCaptureAsync(pipeline.ContainerId, source.Id, null, capture, envelope)).ValueOrThrow();
    }

    private sealed class RecordingProcessor(int? rejectedPromptVersion = null, Func<CancellationToken, Task>? beforeAnswer = null,
        int? unavailablePromptVersion = null)
        : ICaptureProcessor
    {
        public string Key => "fixture-model@1";
        public bool RequiresAi => true;
        public List<(Guid CaptureId, int? PromptVersion)> Calls { get; } = [];

        public Task<AppError?> ValidateConfigurationAsync(PipelineStageOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(options.PromptVersion == unavailablePromptVersion && unavailablePromptVersion is not null
                ? AppErrorFactory.Validation("The pinned prompt is unavailable.") : null);

        public async Task<CaptureProcessResult> ProcessAsync(CaptureProcessInput input, CancellationToken cancellationToken = default)
        {
            Calls.Add((input.CaptureId, input.PromptVersion));
            if (beforeAnswer is not null)
                await beforeAnswer(cancellationToken);
            if (rejectedPromptVersion is { } rejected && input.PromptVersion == rejected)
                return new(new StepResult { Outcome = FetchOutcome.UnexpectedContent }, Retryable: false);
            return new(new StepResult { Outcome = FetchOutcome.Ok },
                JsonSerializer.SerializeToElement(new { promptVersion = input.PromptVersion }));
        }
    }
}
