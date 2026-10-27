namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Replaces a pipeline's display name, saved scope and cadence. Container, source, limits, step overrides,
/// lifecycle and previously queued runs are preserved. Omitted work kinds become disabled schedules.</summary>
public sealed record PipelineUpdate
{
    /// <summary>Gets the display name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the complete saved URL and query parameter overrides.</summary>
    public required PipelineScope Scope { get; init; }

    /// <summary>Gets the complete cadence configuration, at most one row per work kind.</summary>
    public required IReadOnlyList<ScheduleDeclaration> Schedules { get; init; }
}
