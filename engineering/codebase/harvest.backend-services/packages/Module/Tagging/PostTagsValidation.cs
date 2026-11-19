using System.Text;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Represents an answer checked against the active taxonomy: every stored node exists, topics are normalized
/// English slugs, and whatever did not fit is a flag (scfd-entity-model #9).</summary>
internal sealed record ValidatedPostTags(
    string EntityType,
    IReadOnlyList<PostTagsDomain> Domains,
    IReadOnlyList<string> Topics,
    string? About,
    string Timeframe,
    int Items,
    string? Language,
    decimal Confidence,
    string? TitleEn,
    IReadOnlyList<PostTagsFlag> Flags);

/// <summary>Checks a model answer against the active taxonomy before anything is stored.</summary>
internal static partial class PostTagsValidation
{
    internal const int MaxDomains = 3;
    internal const int MaxTopics = 8;
    internal const int MaxTopicLength = 40;
    internal const int MaxFlags = 10;
    internal const string UnknownNode = "unknown taxonomy node";
    internal const string NotEnglish = "topic is not an English slug";

    /// <summary>Validates <paramref name="answer"/>: an unknown node becomes an unsure <c>domains</c> flag carrying the
    /// value, never a stored node; one known node is primary; an <c>unsure</c> pick always carries a flag.</summary>
    internal static ValidatedPostTags Validate(PostTagsAnswer answer, IReadOnlySet<string> activeNodes)
    {
        var flags = new List<PostTagsFlag>();
        foreach (var flag in answer.Flags ?? [])
            if (!string.IsNullOrWhiteSpace(flag.Field))
                flags.Add(new(Clip(flag.Field.Trim(), 40)!, Clip(flag.Reason?.Trim(), 200) ?? "", Clip(flag.Proposed?.Trim(), 120)));

        var domains = new List<PostTagsDomain>();
        foreach (var domain in answer.Domains ?? [])
        {
            string node = (domain.Node ?? "").Trim().ToLowerInvariant();
            if (node.Length == 0 || domains.Any(known => known.Node == node))
                continue;
            if (!activeNodes.Contains(node))
            {
                flags.Add(new("domains", UnknownNode, Clip(node, 120)));
                continue;
            }
            if (domains.Count < MaxDomains)
                domains.Add(new(node, domain.Primary));
        }
        int primary = domains.FindIndex(domain => domain.Primary);
        domains = [.. domains.Select((domain, index) => domain with { Primary = index == (primary < 0 ? 0 : primary) })];
        domains = [.. domains.OrderByDescending(domain => domain.Primary)];

        var topics = new List<string>();
        foreach (var raw in answer.Topics ?? [])
        {
            string? topic = Slug(raw);
            if (topic is null)
            {
                if (!string.IsNullOrWhiteSpace(raw))
                    flags.Add(new("topics", NotEnglish, Clip(raw.Trim(), 120)));
                continue;
            }
            if (!topics.Contains(topic) && topics.Count < MaxTopics)
                topics.Add(topic);
        }

        string entityType = answer.EntityType.ToString().ToLowerInvariant();
        string timeframe = answer.Timeframe.ToString().ToLowerInvariant();
        if (answer.EntityType == PostEntityType.Unsure && !flags.Any(flag => flag.Field == "entityType"))
            flags.Add(new("entityType", "the model chose unsure", null));
        if (answer.Timeframe == PostTimeframe.Unsure && !flags.Any(flag => flag.Field == "timeframe"))
            flags.Add(new("timeframe", "the model chose unsure", null));

        string? language = answer.Language?.Trim().ToLowerInvariant();
        decimal confidence = double.IsFinite(answer.Confidence) ? Math.Round((decimal)Math.Clamp(answer.Confidence, 0, 1), 3) : 0;
        return new(entityType, domains, topics, Clip(answer.About?.Trim(), 200), timeframe, Math.Clamp(answer.Items, 0, 1000),
            language is not null && Language().IsMatch(language) ? language : null, confidence,
            Clip(answer.TitleEn?.Trim(), 300), [.. flags.Take(MaxFlags)]);
    }

    /// <summary>Normalizes an open topic to an English slug, or returns null when nothing Latin is left.</summary>
    internal static string? Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string folded = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKC).Replace("'", "").Replace("’", "");
        if (folded.Any(character => character > 0x7F && char.IsLetter(character)))
            return null;
        string slug = NonSlug().Replace(folded, "-").Trim('-');
        if (slug.Length == 0 || !slug.Any(char.IsAsciiLetter))
            return null;
        return slug.Length <= MaxTopicLength ? slug : slug[..MaxTopicLength].TrimEnd('-');
    }

    private static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlug();

    [GeneratedRegex("^([a-z]{2,3}|und)$", RegexOptions.CultureInvariant)]
    private static partial Regex Language();
}
