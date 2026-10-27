namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents a request to declare a pipeline: a standing harvest of a source, in a container, with a scope,
/// schedules and step overrides.</summary>
/// <remarks>The pipeline's identity is <c>(container, source, scope key)</c>; declaring the same identity twice is a
/// conflict, so a second consumer joins the first pipeline instead of copying it.</remarks>
public sealed record PipelineDeclaration
{
    /// <summary>Gets the container the pipeline harvests into; it must hold the source.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the source the pipeline harvests.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the part of the source the pipeline harvests; the whole source when null or empty.</summary>
    public PipelineScope? Scope { get; init; }

    /// <summary>Gets the pipeline's display name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the cadence rows, at most one per work kind.</summary>
    public IReadOnlyList<ScheduleDeclaration> Schedules { get; init; } = [];

    /// <summary>Gets the changes to the adapter's steps, by step key.</summary>
    public IReadOnlyDictionary<string, StepOverride> Steps { get; init; } = new Dictionary<string, StepOverride>();

    /// <summary>Gets the per-run limits.</summary>
    public PipelineLimits Limits { get; init; } = new();

    /// <summary>Gets the AI switch.</summary>
    public AiMode Ai { get; init; } = AiMode.Off;

    /// <summary>Gets whether the pipeline keeps running with no subscriber.</summary>
    public bool Pinned { get; init; }
}
