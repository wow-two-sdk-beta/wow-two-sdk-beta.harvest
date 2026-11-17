namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Declares the published content sections a source adapter can discover.</summary>
public interface ISourceSectionCatalog
{
    /// <summary>Returns the supported sections without performing network I/O.</summary>
    IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source);
}

/// <summary>Names an adapter-owned section and its locale and entry point.</summary>
public sealed record SourceSectionDefinition(string Label, string Url, string Locale)
{
    /// <summary>Gets the stable Posts filter value shared by language variants of this section.</summary>
    public string? Key { get; init; }

    /// <summary>Gets whether each discovered URL must start under the listing path.</summary>
    public bool RestrictToPath { get; init; } = true;

    /// <summary>Gets whether source registration creates a passive pipeline for this section.</summary>
    public bool RegisterPassivePipeline { get; init; } = true;
}
