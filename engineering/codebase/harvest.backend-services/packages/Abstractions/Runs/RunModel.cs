using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Represents a durable execution and its progress counters.</summary>
public sealed record RunModel
{
    /// <summary>Gets the run identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the container the run writes.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the pipeline the run executes.</summary>
    public required Guid PipelineId { get; init; }
    /// <summary>Gets the requested work.</summary>
    public required WorkKind Kind { get; init; }
    /// <summary>Gets the run's state.</summary>
    public required RunStatus Status { get; init; }
    /// <summary>Gets when the run was queued.</summary>
    public required DateTimeOffset QueuedAtUtc { get; init; }
    /// <summary>Gets when work first started.</summary>
    public DateTimeOffset? StartedAtUtc { get; init; }
    /// <summary>Gets when the run finished.</summary>
    public DateTimeOffset? FinishedAtUtc { get; init; }
    /// <summary>Gets the number of discovered addresses.</summary>
    public int Discovered { get; init; }
    /// <summary>Gets the number of changed captures.</summary>
    public int Captured { get; init; }
    /// <summary>Gets the number of unchanged captures.</summary>
    public int Unchanged { get; init; }
    /// <summary>Gets the number of failed steps.</summary>
    public int Failed { get; init; }
    /// <summary>Gets the number of completed work items.</summary>
    public int Pages { get; init; }
    /// <summary>Gets the number of HTTP requests.</summary>
    public int Requests { get; init; }
    /// <summary>Gets the bytes read.</summary>
    public long Bytes { get; init; }
    /// <summary>Gets the last refusal or error outcome.</summary>
    public FetchOutcome? FetchOutcome { get; init; }
    /// <summary>Gets why a run stopped.</summary>
    public RunReason? CompletionReason { get; init; }
    /// <summary>Gets the latest progress message.</summary>
    public string? Message { get; init; }
}
