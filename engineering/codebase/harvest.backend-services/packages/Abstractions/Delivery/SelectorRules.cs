using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Holds the limits, validation and tree rules of a <see cref="SubscriptionSelector"/>, shared by every form so a
/// selector means the same in the module, the fake, the preview and a diff.</summary>
/// <remarks>The module adds what needs the store: a domain key must be an active taxonomy node, and a source ref must
/// name a source, group or pipeline of the subscription's container.</remarks>
public static partial class SelectorRules
{
    /// <summary>Holds the most domain includes a selector names.</summary>
    public const int MaxDomains = 50;

    /// <summary>Holds the most exceptions one domain include names.</summary>
    public const int MaxExcept = 20;

    /// <summary>Holds the most topic terms, and the most excluded topic terms, a selector names.</summary>
    public const int MaxTopics = 100;

    /// <summary>Holds the most excluded domain keys a selector names.</summary>
    public const int MaxExcludeDomains = 50;

    /// <summary>Holds the most excluded timeframes a selector names; the values are <see cref="Timeframes"/>.</summary>
    public const int MaxExcludeTimeframes = 5;

    /// <summary>Holds the most refs in a selector's include list, and in its exclude list.</summary>
    public const int MaxRefs = 200;

    /// <summary>Holds the most context pairs one include ref carries.</summary>
    public const int MaxRefContext = 32;

    /// <summary>Holds the longest topic term, the Layer 1 slug length.</summary>
    public const int MaxTopicLength = 40;

    /// <summary>Holds the longest context key.</summary>
    public const int MaxContextKeyLength = 64;

    /// <summary>Holds the longest context value.</summary>
    public const int MaxContextValueLength = 512;

    /// <summary>Holds how many days back a selector change re-judges live posts when it names no window.</summary>
    public const int DefaultWindowDays = 30;

    /// <summary>Holds the furthest back a change window or a preview may reach, in days.</summary>
    public const int MaxWindowDays = 365;

    /// <summary>Holds the most posts a change window may judge; a larger window is refused.</summary>
    public const int MaxWindowPosts = 10_000;

    /// <summary>Gets the entity types a selector can require, as Layer 1 tags them.</summary>
    public static IReadOnlyList<string> EntityTypes { get; } = ["event", "listing"];

    /// <summary>Gets the timeframes a selector can exclude, as Layer 1 tags them.</summary>
    public static IReadOnlyList<string> Timeframes { get; } = ["upcoming", "ongoing", "past", "evergreen", "unsure"];

    /// <summary>Returns the first problem in the selector in plain English, or null when it is valid.</summary>
    /// <remarks>Judges the selector as given: <see cref="Normalize"/> first, so case and duplicates do not refuse it.</remarks>
    public static string? Validate(SubscriptionSelector selector)
    {
        if (selector is null)
            return "A selector is required.";

        foreach (string type in Items(selector.EntityTypes))
            if (!EntityTypes.Contains(type))
                return $"Entity type '{type}' is not one of {string.Join(", ", EntityTypes)}.";

        var domains = Items(selector.Domains);
        if (domains.Count > MaxDomains)
            return $"A selector names at most {MaxDomains} domains.";
        foreach (var domain in domains)
        {
            if (domain is null || !IsKey(domain.Key))
                return $"Domain key '{domain?.Key}' is not a taxonomy key: lowercase words joined by '-' and '/'.";
            var except = Items(domain.Except);
            if (except.Count > MaxExcept)
                return $"Domain '{domain.Key}' names at most {MaxExcept} exceptions.";
            foreach (string excluded in except)
            {
                if (!IsKey(excluded))
                    return $"Exception '{excluded}' is not a taxonomy key: lowercase words joined by '-' and '/'.";
                if (!excluded.StartsWith(domain.Key + "/", StringComparison.Ordinal))
                    return $"Exception '{excluded}' is not strictly under domain '{domain.Key}'.";
            }
        }

        if (Items(selector.Topics).Count > MaxTopics)
            return $"A selector names at most {MaxTopics} topics.";
        if (Items(selector.ExcludeTopics).Count > MaxTopics)
            return $"A selector excludes at most {MaxTopics} topics.";
        foreach (string topic in Items(selector.Topics).Concat(Items(selector.ExcludeTopics)))
            if (!IsTopic(topic))
                return $"Topic '{topic}' is not a lowercase word, or words joined by '-', of at most {MaxTopicLength} characters.";

        var excludeDomains = Items(selector.ExcludeDomains);
        if (excludeDomains.Count > MaxExcludeDomains)
            return $"A selector excludes at most {MaxExcludeDomains} domains.";
        foreach (string key in excludeDomains)
            if (!IsKey(key))
                return $"Excluded domain key '{key}' is not a taxonomy key: lowercase words joined by '-' and '/'.";

        var excludeTimeframes = Items(selector.ExcludeTimeframes);
        if (excludeTimeframes.Count > MaxExcludeTimeframes)
            return $"A selector excludes at most {MaxExcludeTimeframes} timeframes.";
        foreach (string timeframe in excludeTimeframes)
            if (!Timeframes.Contains(timeframe))
                return $"Timeframe '{timeframe}' is not one of {string.Join(", ", Timeframes)}.";

        var sources = selector.Sources;
        if (sources is null)
            return null;
        return ValidateRefs(Items(sources.Include), "include", exclude: false) ?? ValidateRefs(Items(sources.Exclude), "exclude", exclude: true);
    }

    /// <summary>Returns the selector in its stored form: values trimmed and lowercased, duplicates dropped in order, nulls
    /// replaced by empty lists, and a domain listed twice kept once, at its first place.</summary>
    /// <remarks>Source ref contexts keep their keys and values as given. A ref that repeats an earlier ref's ids and
    /// context is dropped.</remarks>
    public static SubscriptionSelector Normalize(SubscriptionSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new SubscriptionSelector
        {
            EntityTypes = Clean(selector.EntityTypes),
            Domains = [.. Items(selector.Domains).Where(domain => domain is not null)
                .Select(domain => new SelectorDomain { Key = CleanOne(domain.Key), Except = Clean(domain.Except) })
                .DistinctBy(domain => domain.Key, StringComparer.Ordinal)],
            Topics = Clean(selector.Topics),
            ExcludeDomains = Clean(selector.ExcludeDomains),
            ExcludeTopics = Clean(selector.ExcludeTopics),
            ExcludeTimeframes = Clean(selector.ExcludeTimeframes),
            Sources = new SelectorSources { Include = Refs(selector.Sources?.Include), Exclude = Refs(selector.Sources?.Exclude) },
        };
    }

    /// <summary>Tells whether the value can name a selector subscription: a lowercase letter or digit, then up to 63 more
    /// letters, digits or hyphens.</summary>
    public static bool IsName(string? name) => name is not null && NamePattern().IsMatch(name);

    /// <summary>Tells whether <paramref name="node"/> is <paramref name="key"/> or sits under it.</summary>
    public static bool AtOrUnder(string node, string key) =>
        node == key || node.StartsWith(key + "/", StringComparison.Ordinal);

    /// <summary>Tells whether the term's hyphen tokens occur as one run of the topic's hyphen tokens, so <c>clothing</c>
    /// matches <c>designer-clothing</c> and neither <c>clothingstore</c> nor <c>textiles</c> matches <c>textile</c>.</summary>
    public static bool TokenMatch(string topic, string term)
    {
        if (topic.Length == 0 || term.Length == 0)
            return false;
        string[] tokens = topic.Split('-'), wanted = term.Split('-');
        for (int start = 0; start + wanted.Length <= tokens.Length; start++)
        {
            int offset = 0;
            while (offset < wanted.Length && tokens[start + offset] == wanted[offset])
                offset++;
            if (offset == wanted.Length)
                return true;
        }
        return false;
    }

    /// <summary>Returns the nodes that are not a proper ancestor of another node in the set, in order, so a post tagged
    /// <c>style-fashion</c> and <c>style-fashion/beauty</c> is read as the leaf.</summary>
    public static IReadOnlyList<string> MostSpecific(IEnumerable<string> nodes)
    {
        var set = nodes.Distinct(StringComparer.Ordinal).ToList();
        return [.. set.Where(node => !set.Any(other => other.StartsWith(node + "/", StringComparison.Ordinal)))];
    }

    /// <summary>Returns the (entity type, domain key) pairs the selector wants extracted: each required type, or both
    /// <see cref="EntityTypes"/> when none is required, crossed with each domain include's key. Exceptions, topics and
    /// exclusions add none.</summary>
    public static IReadOnlyList<(string EntityType, string Domain)> Pairs(SubscriptionSelector selector)
    {
        IReadOnlyList<string> types = selector.EntityTypes.Count == 0 ? EntityTypes : [.. selector.EntityTypes.Distinct(StringComparer.Ordinal)];
        var keys = selector.Domains.Select(domain => domain.Key).Distinct(StringComparer.Ordinal).ToList();
        return [.. types.SelectMany(type => keys.Select(key => (type, key)))];
    }

    /// <summary>Tells whether the selector judges Layer 1 tags at all: any entity type, domain, topic or exclusion is
    /// set. A selector with none follows its sources alone and never waits for tags.</summary>
    public static bool HasTagClause(SubscriptionSelector selector) =>
        selector.EntityTypes.Count > 0 || selector.Domains.Count > 0 || selector.Topics.Count > 0
        || selector.ExcludeDomains.Count > 0 || selector.ExcludeTopics.Count > 0 || selector.ExcludeTimeframes.Count > 0;

    private static string? ValidateRefs(IReadOnlyList<SelectorSourceRef> refs, string list, bool exclude)
    {
        if (refs.Count > MaxRefs)
            return $"The {list} list names at most {MaxRefs} source refs.";
        foreach (var reference in refs)
        {
            if (reference is null || (reference.GroupId.HasValue ? 1 : 0) + (reference.SourceId.HasValue ? 1 : 0) + (reference.PipelineId.HasValue ? 1 : 0) != 1)
                return "A source ref names exactly one of groupId, sourceId or pipelineId.";
            var context = reference.Context;
            if (context is null || context.Count == 0)
                continue;
            if (exclude)
                return "An excluded source ref carries no context.";
            if (context.Count > MaxRefContext)
                return $"A source ref's context holds at most {MaxRefContext} pairs.";
            foreach (var (key, value) in context)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Length > MaxContextKeyLength)
                    return $"Context key '{key}' must be 1 to {MaxContextKeyLength} characters.";
                if (value is null || value.Length > MaxContextValueLength)
                    return $"The value of context key '{key}' must be at most {MaxContextValueLength} characters.";
            }
        }
        return null;
    }

    private static IReadOnlyList<T> Items<T>(IReadOnlyList<T>? list) => list ?? [];

    private static string CleanOne(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
        [.. Items(values).Select(CleanOne).Distinct(StringComparer.Ordinal)];

    private static IReadOnlyList<SelectorSourceRef> Refs(IReadOnlyList<SelectorSourceRef>? refs) =>
    [
        .. Items(refs).Where(reference => reference is not null)
            .Select(reference => reference with { Context = new Dictionary<string, string>(reference.Context ?? new Dictionary<string, string>(), StringComparer.Ordinal) })
            .DistinctBy(reference => string.Join('|', reference.GroupId, reference.SourceId, reference.PipelineId,
                string.Join('\u0001', reference.Context.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + '\u0002' + pair.Value)))),
    ];

    private static bool IsKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    private static bool IsTopic(string? topic) => topic is not null && topic.Length <= MaxTopicLength && TopicPattern().IsMatch(topic);

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TopicPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,63}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
