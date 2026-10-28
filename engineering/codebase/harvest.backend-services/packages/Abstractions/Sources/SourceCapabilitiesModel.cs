using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Describes the installed implementation selected for a visible source on this runtime.</summary>
public sealed record SourceCapabilitiesModel
{
    /// <summary>Gets the source whose capabilities were resolved.</summary>
    public required Guid SourceId { get; init; }
    /// <summary>Gets the selected adapter key, or null when none serves the source.</summary>
    public string? AdapterKey { get; init; }
    /// <summary>Gets the work kinds declared by the selected adapter.</summary>
    public required IReadOnlyList<WorkKind> WorkKinds { get; init; }
    /// <summary>Gets the adapter's safe configuration metadata.</summary>
    public required SourceConfigurationModel Configuration { get; init; }
}
