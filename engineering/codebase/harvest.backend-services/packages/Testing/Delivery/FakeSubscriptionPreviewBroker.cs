using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Counts what a selector would deliver over a <see cref="FakePostBroker"/>'s live posts for consumer tests, by the
/// module's rules: each post is judged once with <see cref="SubscriptionSelectorMatcher"/>, a clause's adds read off the
/// matched posts' clauses, and its removes judge the refused posts again against the selector without that exclusion.</summary>
public sealed class FakeSubscriptionPreviewBroker : ISubscriptionPreviewBroker
{
    private const int MaxSamples = 50;
    private const int MaxNearMisses = 10;
    private const int MaxPosts = 5_000;
    private const string TypeClause = "type";
    private const string ExceptPrefix = "except:";
    private const string ExcludeDomainPrefix = "exclude-domain:";
    private const string ExcludeTopicPrefix = "exclude-topic:";
    private const string ExcludeTimeframePrefix = "exclude-timeframe:";
    private const string DomainPrefix = "domain:";
    private const string TopicPrefix = "topic:";
    private const string PostTagsExtras = "postTags";

    private static readonly IReadOnlyDictionary<string, string> NoContext = new Dictionary<string, string>();

    private readonly FakePostBroker posts;
    private readonly IRegistrationBroker registry;
    private readonly TimeProvider time;

    /// <summary>Creates a fake over the same registry and post store used by the consumer's other tests.</summary>
    public FakeSubscriptionPreviewBroker(FakePostBroker posts, IRegistrationBroker registry, TimeProvider? clock = null)
    {
        this.posts = posts; this.registry = registry; time = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result<SubscriptionPreviewModel>> PreviewAsync(SubscriptionPreviewRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request?.Selector is null) return Invalid("A preview needs a selector.");
        if (request.Samples is < 0 or > MaxSamples) return Invalid($"Samples must be between 0 and {MaxSamples}.");
        if (request.Schema is { Length: > 200 }) return Invalid("The schema identifier is too long.");
        var now = time.GetUtcNow();
        var since = request.SinceUtc?.ToUniversalTime() ?? now.AddDays(-SelectorRules.DefaultWindowDays);
        if (since > now) return Invalid("The preview window cannot start in the future.");
        if (since < now.AddDays(-SelectorRules.MaxWindowDays))
            return Invalid($"The preview window reaches at most {SelectorRules.MaxWindowDays} days back.");
        var selector = SelectorRules.Normalize(request.Selector);
        if (SelectorRules.Validate(selector) is { } problem) return Invalid(problem);

        if (await registry.GetTenantAsync(request.TenantId, cancellationToken) is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return Missing("Active tenant not found.");
        if (await registry.GetContainerAsync(request.ContainerId, cancellationToken) is not Result<ContainerModel>.Success { Value: var container }
            || (container.Kind != ContainerKind.Pool && container.TenantId != request.TenantId))
            return Missing("Container not found for the tenant.");
        if (await FakeDeliveryBroker.RefsProblemAsync(registry, request.ContainerId, selector, cancellationToken) is { } refusal) return Invalid(refusal);

        var sources = await registry.ListSourcesAsync(request.ContainerId, cancellationToken) is Result<IReadOnlyList<SourceModel>>.Success { Value: var listed }
            ? listed.ToDictionary(source => source.Id) : [];
        var scope = FakeDeliveryBroker.ReadScope(registry, request.ContainerId, [selector]);
        List<PostModel> window;
        lock (posts.Gate)
            window = [.. posts.Snapshot(request.ContainerId).Where(post => post.MergedInto is null && post.State == PostState.Live && post.LastChangedAtUtc >= since)
                .OrderByDescending(post => post.LastChangedAtUtc).ThenByDescending(post => post.Id).Take(MaxPosts + 1)];
        bool truncated = window.Count > MaxPosts;
        if (truncated) window.RemoveAt(window.Count - 1);

        return Result<SubscriptionPreviewModel>.Ok(Judge(selector, request, since, window, truncated, scope.Groups, scope.Sections, sources));
    }

    private static SubscriptionPreviewModel Judge(SubscriptionSelector selector, SubscriptionPreviewRequest request, DateTimeOffset since,
        IReadOnlyList<PostModel> window, bool truncated, IReadOnlyDictionary<Guid, Guid?> groups,
        IReadOnlyDictionary<Guid, SelectorSection> sections, IReadOnlyDictionary<Guid, SourceModel> sources)
    {
        var relaxations = Relaxations(selector);
        bool anySourceClause = selector.Sources.Include.Count > 0 || selector.Sources.Exclude.Count > 0;
        var sourceOnly = new SubscriptionSelector { Sources = selector.Sources };
        var adds = new Dictionary<string, int>(StringComparer.Ordinal);
        var removes = new Dictionary<string, int>(StringComparer.Ordinal);
        var perSource = new Dictionary<Guid, int[]>();
        var samples = new List<PreviewPost>();
        var nearMisses = new List<PreviewPost>();
        int tagged = 0, matched = 0, noMatch = 0, pending = 0;

        foreach (var post in window)
        {
            var facts = FakeDeliveryBroker.Facts(post, groups);
            var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, facts, sections, request.Schema, NoContext);
            if (post.Base.Tags.Any(tag => tag.Tier == ExtractionTier.SmallModel)) tagged++;
            switch (evaluation.Verdict)
            {
                case SelectorVerdict.Match: matched++; break;
                case SelectorVerdict.NoMatch: noMatch++; break;
                default: pending++; break;
            }

            if (ContextSource(facts, anySourceClause, sourceOnly, sections) is { } sourceId)
            {
                if (!perSource.TryGetValue(sourceId, out var counts)) perSource[sourceId] = counts = new int[3];
                counts[0]++;
                if (evaluation.Verdict == SelectorVerdict.Match) counts[1]++;
                if (evaluation.Verdict == SelectorVerdict.Pending) counts[2]++;
            }

            if (evaluation.Verdict == SelectorVerdict.Match)
            {
                var subjects = evaluation.Clauses.Where(clause => clause.StartsWith(DomainPrefix, StringComparison.Ordinal)
                    || clause.StartsWith(TopicPrefix, StringComparison.Ordinal)).ToList();
                if (subjects.Count == 1)
                    adds[subjects[0]] = adds.GetValueOrDefault(subjects[0]) + 1;
                if (samples.Count < request.Samples)
                    samples.Add(Named(post, SelectorVerdict.Match, evaluation.Clauses));
                continue;
            }

            var admitting = new List<string>();
            foreach (string name in evaluation.Clauses.Select(RelaxationName).OfType<string>().Distinct(StringComparer.Ordinal))
                if (relaxations.TryGetValue(name, out var relaxed)
                    && SubscriptionSelectorMatcher.Evaluate(relaxed, facts, sections, request.Schema, NoContext).Verdict == SelectorVerdict.Match)
                {
                    removes[name] = removes.GetValueOrDefault(name) + 1;
                    admitting.Add(name);
                }
            if (evaluation.Verdict == SelectorVerdict.NoMatch && admitting.Count > 0 && nearMisses.Count < MaxNearMisses)
                nearMisses.Add(Named(post, SelectorVerdict.NoMatch, admitting));
        }

        return new SubscriptionPreviewModel
        {
            SinceUtc = since, Considered = window.Count, Tagged = tagged, Matched = matched, NoMatch = noMatch, Pending = pending,
            Truncated = truncated,
            Sources = [.. perSource.OrderByDescending(pair => pair.Value[0]).ThenBy(pair => pair.Key)
                .Select(pair => new PreviewSourceCount
                {
                    SourceId = pair.Key, Name = sources.GetValueOrDefault(pair.Key)?.Name, GroupId = sources.GetValueOrDefault(pair.Key)?.GroupId,
                    Considered = pair.Value[0], Matched = pair.Value[1], Pending = pair.Value[2],
                })],
            Clauses = [.. ClauseNames(selector).Select(clause => new PreviewClauseCount
            {
                Clause = clause, Adds = adds.GetValueOrDefault(clause), Removes = removes.GetValueOrDefault(clause),
            })],
            Samples = samples, NearMisses = nearMisses,
        };
    }

    /// <summary>Names the source of the post's context link: the title's link when the selector's sources admit it, else the
    /// first admitted link; null when none is admitted, so a post outside the selector's sources joins no row.</summary>
    private static Guid? ContextSource(SelectorPostFacts facts, bool anySourceClause, SubscriptionSelector sourceOnly,
        IReadOnlyDictionary<Guid, SelectorSection> sections)
    {
        if (facts.Links.Count == 0) return null;
        var admitted = anySourceClause
            ? facts.Links.Where(link => SubscriptionSelectorMatcher.Evaluate(sourceOnly,
                new SelectorPostFacts { Envelope = AdmissionEnvelope, Links = [link] }, sections, null, NoContext).Verdict == SelectorVerdict.Match).ToList()
            : [.. facts.Links];
        return (admitted.FirstOrDefault(link => link.LinkId == facts.PrimaryLinkId) ?? admitted.FirstOrDefault())?.SourceId;
    }

    private static readonly PostEnvelope AdmissionEnvelope = new() { Extraction = new ExtractionStamp { NormVersion = "preview", RulesVersion = "preview" } };

    /// <summary>Lists every term the selector names as a clause row: the type, domains, topics, then each exclusion.</summary>
    private static IReadOnlyList<string> ClauseNames(SubscriptionSelector selector)
    {
        var names = new List<string>();
        if (selector.EntityTypes.Count > 0) names.Add(TypeClause);
        names.AddRange(selector.Domains.Select(domain => DomainPrefix + domain.Key));
        names.AddRange(selector.Topics.Select(topic => TopicPrefix + topic));
        names.AddRange(selector.Domains.SelectMany(domain => domain.Except).Select(except => ExceptPrefix + except));
        names.AddRange(selector.ExcludeDomains.Select(key => ExcludeDomainPrefix + key));
        names.AddRange(selector.ExcludeTopics.Select(term => ExcludeTopicPrefix + term));
        names.AddRange(selector.ExcludeTimeframes.Select(value => ExcludeTimeframePrefix + value));
        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Builds, per exclusion, the selector with that exclusion alone removed.</summary>
    private static Dictionary<string, SubscriptionSelector> Relaxations(SubscriptionSelector selector)
    {
        var relaxations = new Dictionary<string, SubscriptionSelector>(StringComparer.Ordinal);
        if (selector.EntityTypes.Count > 0)
            relaxations[TypeClause] = selector with { EntityTypes = [] };
        foreach (string except in selector.Domains.SelectMany(domain => domain.Except).Distinct(StringComparer.Ordinal))
            relaxations[ExceptPrefix + except] = selector with
            {
                Domains = [.. selector.Domains.Select(domain => domain with { Except = [.. domain.Except.Where(key => key != except)] })],
            };
        foreach (string key in selector.ExcludeDomains)
            relaxations[ExcludeDomainPrefix + key] = selector with { ExcludeDomains = [.. selector.ExcludeDomains.Where(other => other != key)] };
        foreach (string term in selector.ExcludeTopics)
            relaxations[ExcludeTopicPrefix + term] = selector with { ExcludeTopics = [.. selector.ExcludeTopics.Where(other => other != term)] };
        foreach (string value in selector.ExcludeTimeframes)
            relaxations[ExcludeTimeframePrefix + value] = selector with { ExcludeTimeframes = [.. selector.ExcludeTimeframes.Where(other => other != value)] };
        return relaxations;
    }

    /// <summary>Maps a clause the matcher gave a refused post to the clause row whose removal could admit it.</summary>
    private static string? RelaxationName(string clause) =>
        clause is "unsure-type" || clause.StartsWith("no-type:", StringComparison.Ordinal) ? TypeClause
        : clause.StartsWith(ExceptPrefix, StringComparison.Ordinal) || clause.StartsWith(ExcludeDomainPrefix, StringComparison.Ordinal)
            || clause.StartsWith(ExcludeTopicPrefix, StringComparison.Ordinal) || clause.StartsWith(ExcludeTimeframePrefix, StringComparison.Ordinal) ? clause
        : null;

    private static PreviewPost Named(PostModel post, SelectorVerdict verdict, IReadOnlyList<string> clauses) => new()
    {
        PostId = post.Id, Revision = post.Revision, Title = post.Base.Title, TitleEn = TitleEn(post.Base), Verdict = verdict,
        Clauses = [.. clauses], LastChangedAtUtc = post.LastChangedAtUtc,
    };

    private static string? TitleEn(PostEnvelope envelope) =>
        envelope.Extras.TryGetValue(PostTagsExtras, out var tags) && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("titleEn", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;

    private static Result<SubscriptionPreviewModel> Invalid(string message) => Result<SubscriptionPreviewModel>.Fail(AppErrorFactory.Validation(message));
    private static Result<SubscriptionPreviewModel> Missing(string message) => Result<SubscriptionPreviewModel>.Fail(AppErrorFactory.NotFound(message));
}
