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

    /// <summary>Gets the tags every post this section supplies already carries, or none: the host writes them in code with the
    /// post, so a post whose title and text come only from such sections needs no tagging call.</summary>
    public SectionImpliedTags? ImpliedTags { get; init; }
}

/// <summary>Declares what a source section implies about every post it supplies: the entity type and one taxonomy node.</summary>
/// <remarks>Example: an OLX apartment rental catalog implies <c>listing</c> under <c>real-estate/apartments</c>. The node is a
/// key of the host's taxonomy; a host whose taxonomy lacks it ignores the declaration and tags the post as usual.</remarks>
/// <param name="EntityType">The entity type, <c>event</c> or <c>listing</c>.</param>
/// <param name="Domain">The taxonomy node, such as <c>real-estate/apartments</c>.</param>
public sealed record SectionImpliedTags(string EntityType, string Domain);
