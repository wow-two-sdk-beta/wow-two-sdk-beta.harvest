using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents a selector's judgment of one revision: the verdict, the clauses that decided it and the context its
/// matching links add.</summary>
/// <param name="Verdict">What the selector said.</param>
/// <param name="Clauses">The clauses behind the verdict, in evaluation order.</param>
/// <param name="Context">The subscription's context merged with the contexts of the include refs that matched the context
/// link; empty when no link was eligible.</param>
public sealed record SelectorEvaluation(SelectorVerdict Verdict, IReadOnlyList<string> Clauses, IReadOnlyDictionary<string, string> Context);

/// <summary>Judges a post revision against a <see cref="SubscriptionSelector"/> and decides what a selector subscription's
/// log appends; pure, so the module, the fake, the preview and a diff share one answer.</summary>
public static class SubscriptionSelectorMatcher
{
    private const string TypePrefix = "type:";
    private const string DomainPrefix = "domain:";
    private const string TopicPrefix = "topic:";
    private const string TimeframePrefix = "timeframe:";

    /// <summary>Judges one revision. The steps run in this order, and the first that decides ends the judgment:
    /// the event schema's kind, the sources, the Layer 1 tags, the exclusions, then the domain and topic includes.</summary>
    /// <param name="selector">The selector, normalized.</param>
    /// <param name="post">The revision's envelope and links.</param>
    /// <param name="sections">The pipelines the selector's section refs name, by pipeline id.</param>
    /// <param name="schema">The subscription's schema; <c>wow2.event.v1</c> refuses a post that is not an event.</param>
    /// <param name="subscriptionContext">The subscription's own context, the lowest layer of the merged context.</param>
    public static SelectorEvaluation Evaluate(SubscriptionSelector selector, SelectorPostFacts post,
        IReadOnlyDictionary<Guid, SelectorSection> sections, string? schema, IReadOnlyDictionary<string, string> subscriptionContext)
    {
        if (schema == PostEnvelopeConstants.EventSchema && post.Envelope.Kind != PostKind.Event)
            return Verdict(SelectorVerdict.NoMatch, "no-schema");

        var include = selector.Sources.Include;
        var exclude = selector.Sources.Exclude;
        var eligible = post.Links.Where(link => (include.Count == 0 || include.Any(reference => Matches(reference, link, sections)))
            && !exclude.Any(reference => Matches(reference, link, sections))).ToList();
        if (eligible.Count == 0)
            return Verdict(SelectorVerdict.NoMatch, "no-source");

        var contextLink = eligible.FirstOrDefault(link => link.LinkId == post.PrimaryLinkId) ?? eligible[0];
        var context = MergeContext(subscriptionContext, include.Where(reference => Matches(reference, contextLink, sections)).ToList());

        if (!SelectorRules.HasTagClause(selector))
            return new(SelectorVerdict.Match, ["sources"], context);

        var small = post.Envelope.Tags.Where(tag => tag.Tier == ExtractionTier.SmallModel).Select(tag => tag.Tag).ToList();
        if (small.Count == 0)
            return new(SelectorVerdict.Pending, ["untagged"], context);

        string? type = Values(small, TypePrefix).FirstOrDefault();
        if (selector.EntityTypes.Count > 0)
        {
            if (type is null)
                return new(SelectorVerdict.Pending, ["unsure-type"], context);
            if (!selector.EntityTypes.Contains(type))
                return new(SelectorVerdict.NoMatch, [$"no-type:{type}"], context);
        }

        var nodes = SelectorRules.MostSpecific(Values(small, DomainPrefix));
        var topics = Values(small, TopicPrefix).ToList();

        if (selector.ExcludeDomains.FirstOrDefault(key => nodes.Any(node => SelectorRules.AtOrUnder(node, key))) is { } excludedDomain)
            return new(SelectorVerdict.NoMatch, [$"exclude-domain:{excludedDomain}"], context);
        if (selector.ExcludeTopics.FirstOrDefault(term => topics.Any(topic => SelectorRules.TokenMatch(topic, term))) is { } excludedTopic)
            return new(SelectorVerdict.NoMatch, [$"exclude-topic:{excludedTopic}"], context);
        if (Values(small, TimeframePrefix).FirstOrDefault() is { } timeframe && selector.ExcludeTimeframes.Contains(timeframe))
            return new(SelectorVerdict.NoMatch, [$"exclude-timeframe:{timeframe}"], context);

        string[] typeClause = selector.EntityTypes.Count > 0 ? [TypePrefix + type] : [];
        if (selector.Domains.Count == 0 && selector.Topics.Count == 0)
            return new(SelectorVerdict.Match, typeClause, context);

        var hits = new List<string>();
        var blocked = new List<string>();
        foreach (var domain in selector.Domains)
        {
            var under = nodes.Where(node => SelectorRules.AtOrUnder(node, domain.Key)).ToList();
            if (under.Any(node => !domain.Except.Any(except => SelectorRules.AtOrUnder(node, except))))
                hits.Add(DomainPrefix + domain.Key);
            else
                blocked.AddRange(domain.Except.Where(except => under.Any(node => SelectorRules.AtOrUnder(node, except)))
                    .Select(except => $"except:{except}"));
        }
        hits.AddRange(selector.Topics.Where(term => topics.Any(topic => SelectorRules.TokenMatch(topic, term))).Select(term => TopicPrefix + term));

        if (hits.Count > 0)
            return new(SelectorVerdict.Match, [.. typeClause, .. hits], context);
        return new(SelectorVerdict.NoMatch, blocked.Count > 0 ? [.. blocked.Distinct(StringComparer.Ordinal)] : ["no-subject"], context);
    }

    /// <summary>Decides which event a selector subscription's log appends for one write, or null for none. Content writes
    /// are <c>post.new</c>, <c>post.changed</c> and <c>post.extracted</c>; every other type is a lifecycle write.</summary>
    /// <param name="type">The type of the write.</param>
    /// <param name="verdict">The selector's verdict on the post's revision.</param>
    /// <param name="seen">Whether the subscription already holds an event of the post.</param>
    /// <param name="unmatched">Whether the subscription's newest content event of the post is <c>post.unmatched</c>.</param>
    /// <param name="stateChanged">Whether the post's state differs from the one its last event carried.</param>
    public static string? Choose(string type, SelectorVerdict verdict, bool seen, bool unmatched, bool stateChanged)
    {
        bool content = type is DeliveryEventTypes.PostNew or DeliveryEventTypes.PostChanged or DeliveryEventTypes.PostExtracted;
        if (!seen)
            return content && verdict == SelectorVerdict.Match ? DeliveryEventTypes.PostNew : null;
        if (!content || verdict == SelectorVerdict.Match)
            return type;
        if (unmatched)
            return null;
        return verdict == SelectorVerdict.NoMatch ? DeliveryEventTypes.PostUnmatched : stateChanged ? type : null;
    }

    private static SelectorEvaluation Verdict(SelectorVerdict verdict, string clause) =>
        new(verdict, [clause], new Dictionary<string, string>());

    private static IEnumerable<string> Values(IEnumerable<string> tags, string prefix) =>
        tags.Where(tag => tag.StartsWith(prefix, StringComparison.Ordinal)).Select(tag => tag[prefix.Length..]);

    private static bool Matches(SelectorSourceRef reference, SelectorLink link, IReadOnlyDictionary<Guid, SelectorSection> sections) =>
        reference.GroupId is { } group ? link.GroupId == group
        : reference.SourceId is { } source ? link.SourceId == source
        : reference.PipelineId is { } pipeline && sections.TryGetValue(pipeline, out var section)
            && section.SourceId == link.SourceId && section.Scope.IncludesUrl(link.Url);

    private static IReadOnlyDictionary<string, string> MergeContext(IReadOnlyDictionary<string, string> subscriptionContext,
        IReadOnlyList<SelectorSourceRef> matched)
    {
        var merged = new Dictionary<string, string>(subscriptionContext, StringComparer.Ordinal);
        foreach (var reference in matched.Where(reference => reference.GroupId.HasValue)
                     .Concat(matched.Where(reference => reference.SourceId.HasValue))
                     .Concat(matched.Where(reference => reference.PipelineId.HasValue)))
            foreach (var (key, value) in reference.Context)
                merged[key] = value;
        return merged;
    }
}
