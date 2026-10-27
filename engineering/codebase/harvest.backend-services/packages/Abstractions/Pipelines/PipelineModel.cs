namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents a declared pipeline with its schedule rows.</summary>
public sealed record PipelineModel
{
    /// <summary>Gets the pipeline's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the container the pipeline harvests into.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the source the pipeline harvests.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the canonical scope: <c>*</c> for the whole source, else the normalized URL and sorted parameters.</summary>
    public required string ScopeKey { get; init; }

    /// <summary>Gets the scope as declared.</summary>
    public required PipelineScope Scope { get; init; }

    /// <summary>Gets the pipeline's display name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the pipeline's lifecycle state.</summary>
    public required PipelineState State { get; init; }

    /// <summary>Gets why the pipeline is paused; null unless <see cref="State"/> is <see cref="PipelineState.Paused"/>.</summary>
    public PipelinePauseReason? PauseReason { get; init; }

    /// <summary>Gets whether the pipeline keeps running with no subscriber.</summary>
    public required bool Pinned { get; init; }

    /// <summary>Gets the AI switch.</summary>
    public required AiMode Ai { get; init; }

    /// <summary>Gets the per-run limits.</summary>
    public required PipelineLimits Limits { get; init; }

    /// <summary>Gets the changes to the adapter's steps, by step key.</summary>
    public required IReadOnlyDictionary<string, StepOverride> Steps { get; init; }

    /// <summary>Gets the cadence rows, ordered by work kind.</summary>
    public required IReadOnlyList<ScheduleModel> Schedules { get; init; }

    /// <summary>Gets when the pipeline was declared (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
