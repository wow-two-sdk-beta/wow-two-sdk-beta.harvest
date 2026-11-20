using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Represents the stored answer a post's resolution writes back: a <c>done</c> row of <c>harvest_post_tags</c>,
/// with the model that gave it, <c>{provider}/{model}</c>, or the rule that wrote it in code instead.</summary>
/// <remarks><see cref="Rule"/> names that rule (<see cref="PostTagsConstants.SectionRule"/>) for tags a source section implies;
/// such a row has no model, prompt version, English title or topics, and its tags carry tier
/// <see cref="ExtractionTier.Rules"/>.</remarks>
internal sealed record StoredPostTags(
    string EntityType,
    IReadOnlyList<PostTagsDomain> Domains,
    IReadOnlyList<string> Topics,
    string? TitleEn,
    decimal? Confidence,
    int? PromptVersion,
    string? Model = null,
    string? Timeframe = null,
    string? Rule = null);

/// <summary>Writes a stored answer onto a resolved envelope, beside the tags the readers mapped.</summary>
/// <remarks>A post's <c>base</c> is derived from its readings on every resolve, so the answer is applied there rather
/// than patched once: a later capture keeps the tags until a new answer replaces them.</remarks>
internal static class PostTagsOverlay
{
    /// <summary>Returns <paramref name="resolved"/> with the answer's tags (tier <see cref="ExtractionTier.SmallModel"/>, or
    /// <see cref="ExtractionTier.Rules"/> for tags a rule wrote) after the mapped ones, and <c>extras.postTags.titleEn</c>.</summary>
    /// <remarks>
    ///   - <c>type:{entityType}</c>, except for <c>unsure</c>, which only flags
    ///   - <c>domain:{node}</c> for each node, primary first, and each node's ancestors, so a tier-1 tag filter matches a
    ///     post tagged deeper
    ///   - <c>topic:{slug}</c> per topic
    ///   - <c>timeframe:{value}</c>, every value, <c>unsure</c> included; a rule judges no timeframe, so its tags have none
    /// </remarks>
    internal static PostEnvelope Apply(PostEnvelope resolved, StoredPostTags? stored)
    {
        if (stored is null)
            return resolved;
        bool rule = stored.Rule is not null;
        string? version = rule ? stored.Rule : stored.PromptVersion is { } prompt ? $"{PostTagsConstants.UseCase}@{prompt}" : null;
        double? score = stored.Confidence is { } confidence ? (double)confidence : null;
        var keys = new List<string>();
        if (stored.EntityType != "unsure")
            keys.Add(PostTagsConstants.TypeTagPrefix + stored.EntityType);
        foreach (var domain in stored.Domains.OrderByDescending(domain => domain.Primary))
            foreach (var node in Lineage(domain.Node))
                keys.Add(PostTagsConstants.DomainTagPrefix + node);
        keys.AddRange(stored.Topics.Select(topic => PostTagsConstants.TopicTagPrefix + topic));
        if (!rule && !string.IsNullOrWhiteSpace(stored.Timeframe))
            keys.Add(PostTagsConstants.TimeframeTagPrefix + stored.Timeframe);

        var tier = rule ? ExtractionTier.Rules : ExtractionTier.SmallModel;
        var tags = resolved.Tags.Where(tag => tag.Tier != ExtractionTier.SmallModel).ToList();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
            tags.Add(new PostTag { Tag = key, Score = score, Tier = tier, Version = version });

        var extras = new Dictionary<string, JsonElement>(resolved.Extras);
        if (!string.IsNullOrWhiteSpace(stored.TitleEn))
            extras[PostTagsConstants.ExtrasNamespace] = JsonSerializer.SerializeToElement(new { titleEn = stored.TitleEn });
        else
            extras.Remove(PostTagsConstants.ExtrasNamespace);
        return resolved with { Tags = tags, Extras = extras };
    }

    /// <summary>Lists a node, then each ancestor up to its domain: <c>a/b/c</c> gives <c>a/b/c</c>, <c>a/b</c>, <c>a</c>.</summary>
    internal static IEnumerable<string> Lineage(string node)
    {
        for (string current = node; current.Length > 0; current = current.LastIndexOf('/') is var cut and > 0 ? current[..cut] : "")
            yield return current;
    }
}
