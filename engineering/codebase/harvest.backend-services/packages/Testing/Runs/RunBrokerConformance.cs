using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Runs;

/// <summary>Specifies network-free execution controls shared by Module, HTTP and consumer fakes.</summary>
public abstract class RunBrokerConformance
{
    /// <summary>Gets the registry that creates isolated resources for each case.</summary>
    protected abstract IRegistrationBroker Registry { get; }
    /// <summary>Gets the run implementation under test; no worker may execute queued work.</summary>
    protected abstract IRunBroker Runs { get; }

    /// <summary>Repeated starts join an active run while inspection preserves its queued state.</summary>
    [Fact]
    public async Task Start_ShouldJoinAnActiveRun_WithoutExecutingIt()
    {
        var context = await ContextAsync();
        var first = Value(await Runs.StartAsync(context.Pipeline));
        var joined = Value(await Runs.StartAsync(context.Pipeline));
        joined.Id.Should().Be(first.Id);
        joined.Status.Should().Be(RunStatus.Queued);
        joined.Kind.Should().Be(WorkKind.Capture);
        joined.ContainerId.Should().Be(context.Container);
        joined.Requests.Should().Be(0);
        Value(await Runs.GetAsync(first.Id)).Id.Should().Be(first.Id);
        Value(await Runs.ListAsync(context.Pipeline)).Should().ContainSingle().Which.Id.Should().Be(first.Id);
    }

    /// <summary>A cancellation can be repeated without discarding the run history.</summary>
    [Fact]
    public async Task Cancel_ShouldRemainIdempotent_AndKeepHistory()
    {
        var context = await ContextAsync();
        var queued = Value(await Runs.StartAsync(context.Pipeline));
        var first = Value(await Runs.CancelAsync(queued.Id));
        var second = Value(await Runs.CancelAsync(queued.Id));
        first.Status.Should().Be(RunStatus.Cancelled);
        first.CompletionReason.Should().Be(RunReason.Cancelled);
        first.FinishedAtUtc.Should().NotBeNull();
        second.FinishedAtUtc.Should().Be(first.FinishedAtUtc);
        Value(await Runs.ListAsync(context.Pipeline)).Should().ContainSingle().Which.Status.Should().Be(RunStatus.Cancelled);
    }

    /// <summary>A paused pipeline refuses a new execution until the operator resumes it.</summary>
    [Fact]
    public async Task SetPaused_ShouldGateStarts_AndUpdateTheRegistry()
    {
        var context = await ContextAsync();
        Value(await Runs.SetPausedAsync(context.Pipeline, true)).State.Should().Be(PipelineState.Paused);
        Value(await Registry.GetPipelineAsync(context.Pipeline)).PauseReason.Should().Be(PipelinePauseReason.Operator);
        Error(await Runs.StartAsync(context.Pipeline)).Type.Should().Be(AppErrorType.Conflict);
        Value(await Runs.SetPausedAsync(context.Pipeline, false)).State.Should().Be(PipelineState.Active);
        Value(await Registry.GetPipelineAsync(context.Pipeline)).PauseReason.Should().BeNull();
        Value(await Runs.StartAsync(context.Pipeline)).Status.Should().Be(RunStatus.Queued);
    }

    /// <summary>List limits apply to the requested pipeline without mixing another pipeline's runs.</summary>
    [Fact]
    public async Task List_ShouldHonorPipelineAndPageLimit()
    {
        var first = await ContextAsync();
        var second = await ContextAsync();
        Value(await Runs.StartAsync(first.Pipeline));
        Value(await Runs.StartAsync(first.Pipeline, WorkKind.Discover));
        Value(await Runs.StartAsync(second.Pipeline));
        Value(await Runs.ListAsync(first.Pipeline)).Should().HaveCount(2).And.OnlyContain(run => run.PipelineId == first.Pipeline);
        Value(await Runs.ListAsync(first.Pipeline, limit: 1)).Should().ContainSingle();
        Error(await Runs.ListAsync(first.Pipeline, limit: 0)).Type.Should().Be(AppErrorType.Validation);
        Error(await Runs.ListAsync(first.Pipeline, limit: 201)).Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>Host health counts only the requested container's queue.</summary>
    [Fact]
    public async Task Hosts_ShouldKeepEachContainersQueuePrivate()
    {
        var first = await ContextAsync();
        var second = await ContextAsync();
        var run = Value(await Runs.StartAsync(first.Pipeline));
        Value(await Runs.HostsAsync(second.Container)).Should().BeEmpty();
        var hosts = Value(await Runs.HostsAsync(first.Container));
        hosts.Should().ContainSingle().Which.HostKey.Should().Be(first.Host);
        hosts[0].Queued.Should().Be(1);
        Value(await Runs.CancelAsync(run.Id));
        Value(await Runs.HostsAsync(first.Container)).Sum(host => host.Queued).Should().Be(0);
    }

    /// <summary>Unknown run identifiers are failures rather than empty or invented runs.</summary>
    [Fact]
    public async Task GetAndCancel_ShouldReportMissingRuns()
    {
        Error(await Runs.GetAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
        Error(await Runs.CancelAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>An unknown pipeline is a missing resource rather than an empty run list.</summary>
    [Fact]
    public async Task List_ShouldReportAMissingPipeline()
    {
        Error(await Runs.ListAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>A pause cannot target a pipeline absent from the registry.</summary>
    [Fact]
    public async Task SetPaused_ShouldReportAMissingPipeline()
    {
        Error(await Runs.SetPausedAsync(Guid.NewGuid(), true)).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>An unknown container is a missing resource rather than an empty host report.</summary>
    [Fact]
    public async Task Hosts_ShouldReportAMissingContainer()
    {
        Error(await Runs.HostsAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>Undefined work kinds are rejected before queueing work.</summary>
    [Fact]
    public async Task Start_ShouldRejectAnUnknownKind()
    {
        var context = await ContextAsync();
        Error(await Runs.StartAsync(context.Pipeline, (WorkKind)999)).Type.Should().Be(AppErrorType.Validation);
        Value(await Runs.ListAsync(context.Pipeline)).Should().BeEmpty();
    }

    private async Task<Context> ContextAsync()
    {
        string unique = Guid.NewGuid().ToString("N");
        var tenant = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "runs-" + unique, Name = "Run conformance" }));
        var container = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        string host = unique + ".example";
        var source = Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Handle = host, Family = SourceFamily.Site,
            Coverage = SourceCoverage.Whole, Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));
        var pipeline = Value(await Registry.DeclarePipelineAsync(new PipelineDeclaration { ContainerId = container.Id, SourceId = source.Id }));
        return new Context(container.Id, pipeline.Id, host);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    private static AppError Error<T>(Result<T> result) where T : notnull => result is Result<T>.Failure failure ? failure.Error
        : throw new Xunit.Sdk.XunitException("Expected a failed result.");
    private sealed record Context(Guid Container, Guid Pipeline, string Host);
}
