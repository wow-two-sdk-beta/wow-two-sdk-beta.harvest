using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Represents the stored answer a post's resolution writes back: a <c>done</c> row of <c>harvest_post_tags</c>.</summary>
internal sealed record StoredPostTags(
    string EntityType,
    IReadOnlyList<PostTagsDomain> Domains,
    IReadOnlyList<string> Topics,
    string? TitleEn,
    decimal? Confidence,
    int? PromptVersion);

/// <summary>Writes a stored answer onto a resolved envelope, beside the tags the readers mapped.</summary>
/// <remarks>A post's <c>base</c> is derived from its readings on every resolve, so the answer is applied there rather
/// than patched once: a later capture keeps the tags until a new answer replaces them.</remarks>
internal static class PostTagsOverlay
{
    /// <summary>Returns <paramref name="resolved"/> with the answer's tags (tier <see cref="ExtractionTier.SmallModel"/>)
    /// after the mapped ones, and <c>extras.postTags.titleEn</c>.</summary>
    /// <remarks>
    ///   - <c>type:{entityType}</c>, except for <c>unsure</c>, which only flags
    ///   - <c>domain:{node}</c> for each node, primary first, and each node's ancestors, so a tier-1 tag filter matches a
    ///     post tagged deeper
    ///   - <c>topic:{slug}</c> per topic
    /// </remarks>
    internal static PostEnvelope Apply(PostEnvelope resolved, StoredPostTags? stored)
    {
        if (stored is null)
            return resolved;
        string? version = stored.PromptVersion is { } prompt ? $"{PostTagsConstants.UseCase}@{prompt}" : null;
        double? score = stored.Confidence is { } confidence ? (double)confidence : null;
        var keys = new List<string>();
        if (stored.EntityType != "unsure")
            keys.Add(PostTagsConstants.TypeTagPrefix + stored.EntityType);
        foreach (var domain in stored.Domains.OrderByDescending(domain => domain.Primary))
            foreach (var node in Lineage(domain.Node))
                keys.Add(PostTagsConstants.DomainTagPrefix + node);
        keys.AddRange(stored.Topics.Select(topic => PostTagsConstants.TopicTagPrefix + topic));

        var tags = resolved.Tags.Where(tag => tag.Tier != ExtractionTier.SmallModel).ToList();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
            tags.Add(new PostTag { Tag = key, Score = score, Tier = ExtractionTier.SmallModel, Version = version });

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
