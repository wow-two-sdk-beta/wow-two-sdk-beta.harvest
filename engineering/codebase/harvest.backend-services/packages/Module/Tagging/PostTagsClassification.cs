using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Turns a post's stored tags into its relevance verdict for the wanted (entity type, domain) pairs: the
/// <c>classification</c> a subscriber's <c>payload</c> classifier reads (events-integration S16).</summary>
/// <remarks>
///   - entity type <c>unsure</c>: <see cref="ClassificationVerdict.Uncertain"/>
///   - the tags match a wanted pair: <see cref="ClassificationVerdict.Relevant"/>
///   - entity type <c>none</c>, or a known domain off every pair: <see cref="ClassificationVerdict.Irrelevant"/>
///   - an event or listing with no known domain (the model's pick was unsure or unknown, so validation kept no node):
///     <see cref="ClassificationVerdict.Uncertain"/>
///   - the confidence is the tags answer's own; the classifier names the model and the prompt version, or the rule for tags a
///     source section implies (<c>rule:section-implied@1</c>, no confidence)
/// </remarks>
internal static class PostTagsClassification
{
    /// <summary>The reason of a verdict whose entity type was <c>unsure</c>.</summary>
    internal const string EntityTypeUnsure = "entity type unsure";

    /// <summary>The reason of a verdict whose primary domain was unsure.</summary>
    internal const string DomainUnsure = "primary domain unsure";

    /// <summary>The reason of a verdict on a post about neither an event nor a listing.</summary>
    internal const string NoEntity = "entity type none";

    /// <summary>The reason of a verdict whose domains match no wanted pair.</summary>
    internal const string OffEveryPair = "off every wanted pair";

    /// <summary>Returns the verdict alone: relevant, irrelevant or uncertain, with its reason; none without wanted pairs.</summary>
    /// <param name="entityType">The tags' entity type.</param>
    /// <param name="domains">The tags' known nodes, primary first.</param>
    /// <param name="wanted">The normalized wanted pairs.</param>
    internal static (ClassificationVerdict Verdict, string Reason)? Verdict(string entityType, IReadOnlyList<PostTagsDomain> domains,
        IReadOnlyList<PostExtractionWant> wanted)
    {
        if (wanted.Count == 0)
            return null;
        if (entityType == "unsure")
            return (ClassificationVerdict.Uncertain, EntityTypeUnsure);
        if (PostExtractionDemand.MatchPair(wanted, entityType, domains) is { } pair)
            return (ClassificationVerdict.Relevant, $"{pair.EntityType} under {pair.Domain}");
        if (entityType == "none")
            return (ClassificationVerdict.Irrelevant, NoEntity);
        return domains.Count == 0 ? (ClassificationVerdict.Uncertain, DomainUnsure) : (ClassificationVerdict.Irrelevant, OffEveryPair);
    }

    /// <summary>Returns the post's classification from its stored tags, or null when it has no tags or nothing is wanted.</summary>
    /// <param name="stored">The stored tags.</param>
    /// <param name="wanted">The normalized wanted pairs.</param>
    /// <param name="format">The <c>format</c> of a done <c>extract-event</c> answer about the same content, when one exists.</param>
    /// <remarks><c>kind</c> is the extraction's format when it names one, else the entity type; an unsure entity type
    /// names no kind.</remarks>
    internal static PostClassification? Classify(StoredPostTags? stored, IReadOnlyList<PostExtractionWant> wanted, string? format)
    {
        if (stored is null || Verdict(stored.EntityType, stored.Domains, wanted) is not { } verdict)
            return null;
        string? kind = format is { Length: > 0 } and not "unsure" ? format
            : stored.EntityType == "unsure" ? null : stored.EntityType;
        return new PostClassification
        {
            Classifier = stored.Rule is { } rule ? $"rule:{rule}"
                : $"model:{stored.Model ?? "unknown"}@{PostTagsConstants.UseCase}:{stored.PromptVersion?.ToString() ?? "unknown"}",
            Verdict = verdict.Verdict,
            Confidence = stored.Confidence is { } confidence ? (double)confidence : null,
            Reason = verdict.Reason,
            Kind = kind,
        };
    }

    /// <summary>Returns <paramref name="resolved"/> with the classification its stored tags give; unchanged when they give
    /// none, so a post without tags keeps exactly its readers' envelope.</summary>
    internal static PostEnvelope Apply(PostEnvelope resolved, StoredPostTags? stored, IReadOnlyList<PostExtractionWant> wanted, string? format) =>
        Classify(stored, wanted, format) is { } classification ? resolved with { Classification = classification } : resolved;
}
