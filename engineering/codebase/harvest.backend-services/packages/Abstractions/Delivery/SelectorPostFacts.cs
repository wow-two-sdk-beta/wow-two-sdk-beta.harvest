using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents one live link of a post as a selector sees it.</summary>
/// <param name="LinkId">The link's identifier.</param>
/// <param name="SourceId">The source the link was read from.</param>
/// <param name="GroupId">The source group of that source, or null when it has none.</param>
/// <param name="Url">The link's URL, tested against a section's path scope.</param>
public sealed record SelectorLink(Guid LinkId, Guid SourceId, Guid? GroupId, string Url);

/// <summary>Represents a pipeline a selector's section ref names: its source and its path scope.</summary>
/// <param name="PipelineId">The pipeline's identifier.</param>
/// <param name="SourceId">The source the pipeline reads.</param>
/// <param name="Scope">What part of that source the pipeline harvests.</param>
public sealed record SelectorSection(Guid PipelineId, Guid SourceId, PipelineScope Scope);

/// <summary>Represents what a selector judges: one revision's envelope and its live links.</summary>
/// <remarks>The envelope is the delivered revision's own, so the verdict judges exactly what the event carries.</remarks>
public sealed record SelectorPostFacts
{
    /// <summary>Gets the revision's resolved envelope, whose <see cref="PostEnvelope.Tags"/> of tier
    /// <see cref="ExtractionTier.SmallModel"/> carry the Layer 1 answer.</summary>
    public required PostEnvelope Envelope { get; init; }

    /// <summary>Gets the post's links, in the post's order.</summary>
    public IReadOnlyList<SelectorLink> Links { get; init; } = [];

    /// <summary>Gets the link that supplied the post's title, which a selector prefers as the context link.</summary>
    public Guid? PrimaryLinkId { get; init; }
}
