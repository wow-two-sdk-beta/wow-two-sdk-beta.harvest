using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>Selects what <see cref="OcharoFashionFilter"/> selects, plus the posts the AI tags call relevant to Ocharo:
/// a <c>relevant</c> classification at <see cref="MinConfidence"/> or above on a post tagged an event under
/// <c>style-fashion</c> (events-integration S16).</summary>
/// <remarks>
///   - the classification comes from the <c>post-tags</c> pass, which marks a post relevant for any wanted pair; the
///     tag check keeps another product's pair, such as Haven's <c>listing</c> + <c>real-estate</c>, out of Ocharo's feed
///   - with tagging off no post carries a classification, so the filter selects exactly what the rules select
/// </remarks>
public sealed class OcharoRelevanceFilter : IDeliveryPostFilter
{
    /// <summary>The stable subscription filter key.</summary>
    public const string FilterKey = "ocharo-relevance-v1";

    /// <summary>The lowest confidence of a relevant verdict the filter accepts.</summary>
    public const double MinConfidence = 0.5;

    /// <summary>Gets Ocharo's wanted pair: an event under <c>style-fashion</c>.</summary>
    public static PostExtractionWant Wanted { get; } = new() { EntityType = "event", Domain = "style-fashion" };

    private readonly OcharoFashionFilter rules = new();

    /// <inheritdoc />
    public string Key => FilterKey;

    /// <inheritdoc />
    public bool Matches(PostEnvelope envelope) => rules.Matches(envelope) || Relevant(envelope);

    /// <summary>Whether the post's classification is relevant at <see cref="MinConfidence"/> or above and its AI tags name
    /// Ocharo's pair.</summary>
    internal static bool Relevant(PostEnvelope envelope) =>
        envelope.Classification is { Verdict: ClassificationVerdict.Relevant, Confidence: >= MinConfidence }
        && HasModelTag(envelope, PostTagsConstants.TypeTagPrefix + Wanted.EntityType)
        && HasModelTag(envelope, PostTagsConstants.DomainTagPrefix + Wanted.Domain);

    private static bool HasModelTag(PostEnvelope envelope, string key) =>
        envelope.Tags.Any(tag => tag.Tier == ExtractionTier.SmallModel && tag.Tag == key);
}
