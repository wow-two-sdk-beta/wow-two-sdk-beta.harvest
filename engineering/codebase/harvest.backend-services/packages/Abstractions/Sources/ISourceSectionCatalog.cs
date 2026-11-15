namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Declares the sections a source adapter can register with passive pipelines.</summary>
public interface ISourceSectionCatalog
{
    /// <summary>Returns the supported sections without performing network I/O.</summary>
    IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source);
}

/// <summary>Names an adapter-owned section and its locale and entry point.</summary>
public sealed record SourceSectionDefinition(string Label, string Url, string Locale)
{
    /// <summary>Gets whether each discovered URL must start under the listing path.</summary>
    public bool RestrictToPath { get; init; } = true;
}
