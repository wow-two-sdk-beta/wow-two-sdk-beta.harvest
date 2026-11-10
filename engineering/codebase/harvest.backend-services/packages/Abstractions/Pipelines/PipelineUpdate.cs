namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Replaces a pipeline's display name, saved scope and cadence, and sets the limits it names. Container, source,
/// step overrides, lifecycle and previously queued runs are preserved. Omitted work kinds become disabled schedules.</summary>
public sealed record PipelineUpdate
{
    /// <summary>Gets the display name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the complete saved URL and query parameter overrides.</summary>
    public required PipelineScope Scope { get; init; }

    /// <summary>Gets the complete cadence configuration, at most one row per work kind.</summary>
    public required IReadOnlyList<ScheduleDeclaration> Schedules { get; init; }

    /// <summary>Gets the limits to set: each stated value replaces the stored one, and an omitted value or no limits
    /// at all keeps it. <see cref="PipelineLimits.MaxPages"/> may be 1–<see cref="PipelineConstants.MaxPagesOnUpdate"/>.</summary>
    public PipelineLimits? Limits { get; init; }
}
