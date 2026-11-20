using System.Data;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Counts what a selector would deliver over one container's live posts, before it is saved.</summary>
/// <remarks>
///   - reads in one repeatable-read transaction and writes nothing
///   - judges the container's live posts that changed since the window's start, newest first, at most
///     <see cref="MaxPosts"/>; posts and links are read in two batches, never one post at a time
///   - judges each post once with <see cref="SubscriptionSelectorMatcher.Evaluate"/>; a clause's <c>adds</c> read off the
///     matched posts' clauses, and its <c>removes</c> judge only the refused posts whose own clauses name that exclusion,
///     again with the matcher, against the selector with that exclusion alone removed
/// </remarks>
internal sealed class ModuleSubscriptionPreviewBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    TimeProvider time) : ISubscriptionPreviewBroker
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

    private static readonly IReadOnlyDictionary<string, string> NoContext = new Dictionary<string, string>();

    /// <inheritdoc />
    public async Task<Result<SubscriptionPreviewModel>> PreviewAsync(SubscriptionPreviewRequest request, CancellationToken cancellationToken = default)
    {
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

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_tenants WHERE id=@id AND state='active'", cancellationToken, ("id", request.TenantId)) is null)
            return Missing("Active tenant not found.");
        if (await PostSql.ScalarAsync(connection, transaction, """
                SELECT 1 FROM harvest_containers WHERE id=@container AND (kind='pool' OR tenant_id=@tenant)
                """, cancellationToken, ("container", request.ContainerId), ("tenant", request.TenantId)) is null)
            return Missing("Container not found for the tenant.");
        if (await ValidateStoreAsync(connection, transaction, request.ContainerId, selector, cancellationToken) is { } refusal)
            return Invalid(refusal);

        var sections = await ReadSectionsAsync(connection, transaction, request.ContainerId, selector, cancellationToken);
        var sources = await ReadSourcesAsync(connection, transaction, request.ContainerId, cancellationToken);
        var (posts, truncated) = await ReadPostsAsync(connection, transaction, request.ContainerId, since, cancellationToken);
        var links = await ReadLinksAsync(connection, transaction, request.ContainerId, posts, sources, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Result<SubscriptionPreviewModel>.Ok(Judge(selector, request, since, posts, truncated, links, sections, sources));
    }

    private static SubscriptionPreviewModel Judge(SubscriptionSelector selector, SubscriptionPreviewRequest request, DateTimeOffset since,
        IReadOnlyList<PostRow> posts, bool truncated, IReadOnlyDictionary<Guid, List<SelectorLink>> links,
        IReadOnlyDictionary<Guid, SelectorSection> sections, IReadOnlyDictionary<Guid, SourceRow> sources)
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

        foreach (var post in posts)
        {
            var postLinks = links.TryGetValue(post.Id, out var found) ? found : [];
            var facts = new SelectorPostFacts { Envelope = post.Envelope, Links = postLinks, PrimaryLinkId = post.PrimaryLinkId };
            var evaluation = SubscriptionSelectorMatcher.Evaluate(selector, facts, sections, request.Schema, NoContext);
            if (post.Envelope.Tags.Any(SelectorRules.IsAnswerTag)) tagged++;
            switch (evaluation.Verdict)
            {
                case SelectorVerdict.Match: matched++; break;
                case SelectorVerdict.NoMatch: noMatch++; break;
                default: pending++; break;
            }

            if (ContextSource(postLinks, post.PrimaryLinkId, anySourceClause, sourceOnly, sections) is { } sourceId)
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
            SinceUtc = since, Considered = posts.Count, Tagged = tagged, Matched = matched, NoMatch = noMatch, Pending = pending,
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

    /// <summary>Names the source of the post's context link: the title's link when the selector's sources admit it, else
    /// the first admitted link; null when none is admitted, so a post outside the selector's sources joins no row.</summary>
    /// <remarks>Admission is asked of the matcher itself, one link at a time, over the selector's sources alone.</remarks>
    private static Guid? ContextSource(IReadOnlyList<SelectorLink> links, Guid? primaryLinkId, bool anySourceClause,
        SubscriptionSelector sourceOnly, IReadOnlyDictionary<Guid, SelectorSection> sections)
    {
        if (links.Count == 0) return null;
        var admitted = anySourceClause
            ? links.Where(link => SubscriptionSelectorMatcher.Evaluate(sourceOnly,
                new SelectorPostFacts { Envelope = AdmissionEnvelope, Links = [link] }, sections, null, NoContext).Verdict == SelectorVerdict.Match).ToList()
            : [.. links];
        return (admitted.FirstOrDefault(link => link.LinkId == primaryLinkId) ?? admitted.FirstOrDefault())?.SourceId;
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

    private static PreviewPost Named(PostRow post, SelectorVerdict verdict, IReadOnlyList<string> clauses) => new()
    {
        PostId = post.Id, Revision = post.Revision, Title = post.Title, TitleEn = post.TitleEn, Verdict = verdict,
        Clauses = [.. clauses], LastChangedAtUtc = post.LastChangedAtUtc,
    };

    /// <summary>Refuses what the selector alone cannot: a domain, exception or excluded-domain key that is not an active
    /// taxonomy node, and a source ref outside the container. Repeats the subscribe path's check in this file.</summary>
    private static async Task<string?> ValidateStoreAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId,
        SubscriptionSelector selector, CancellationToken ct)
    {
        string[] keys = [.. selector.Domains.Select(domain => domain.Key).Concat(selector.Domains.SelectMany(domain => domain.Except))
            .Concat(selector.ExcludeDomains).Distinct(StringComparer.Ordinal)];
        if (keys.Length > 0)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = PostSql.Command(connection, transaction,
                             "SELECT key FROM harvest_taxonomy_nodes WHERE active AND key = ANY(@keys)", ("keys", keys)))
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) known.Add(reader.GetString(0));
            if (keys.FirstOrDefault(key => !known.Contains(key)) is { } unknown)
                return $"Taxonomy key '{unknown}' is not an active node.";
        }

        var refs = selector.Sources.Include.Concat(selector.Sources.Exclude).ToList();
        foreach (var (label, ids, table) in new[]
                 {
                     ("group", Ids(refs, reference => reference.GroupId), "harvest_source_groups"),
                     ("source", Ids(refs, reference => reference.SourceId), "harvest_sources"),
                     ("pipeline", Ids(refs, reference => reference.PipelineId), "harvest_pipelines"),
                 })
        {
            if (ids.Length == 0) continue;
            var present = new HashSet<Guid>();
            await using var command = PostSql.Command(connection, transaction,
                $"SELECT id FROM {table} WHERE container_id=@container AND id = ANY(@ids)", ("container", containerId), ("ids", ids));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) present.Add(reader.GetGuid(0));
            if (ids.Where(id => !present.Contains(id)).Select(id => (Guid?)id).FirstOrDefault() is { } missing)
                return $"The {label} {missing} is not in the container.";
        }
        return null;
    }

    private static Guid[] Ids(IEnumerable<SelectorSourceRef> refs, Func<SelectorSourceRef, Guid?> pick) =>
        [.. refs.Select(pick).Where(id => id.HasValue).Select(id => id!.Value).Distinct()];

    private static async Task<IReadOnlyDictionary<Guid, SelectorSection>> ReadSectionsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid containerId, SubscriptionSelector selector, CancellationToken ct)
    {
        var ids = Ids(selector.Sources.Include.Concat(selector.Sources.Exclude), reference => reference.PipelineId);
        var sections = new Dictionary<Guid, SelectorSection>();
        if (ids.Length == 0) return sections;
        await using var command = PostSql.Command(connection, transaction,
            "SELECT id,source_id,scope::text FROM harvest_pipelines WHERE container_id=@container AND id = ANY(@ids)",
            ("container", containerId), ("ids", ids));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            sections[reader.GetGuid(0)] = new SelectorSection(reader.GetGuid(0), reader.GetGuid(1),
                StoreJson.Read(reader.GetString(2), new PipelineScope()));
        return sections;
    }

    private static async Task<IReadOnlyDictionary<Guid, SourceRow>> ReadSourcesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid containerId, CancellationToken ct)
    {
        var sources = new Dictionary<Guid, SourceRow>();
        await using var command = PostSql.Command(connection, transaction,
            "SELECT id,group_id,display_name FROM harvest_sources WHERE container_id=@container", ("container", containerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            sources[reader.GetGuid(0)] = new SourceRow(reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        return sources;
    }

    /// <summary>Reads the window's live posts, newest first, one more than the cap so a longer window is noticed.</summary>
    private static async Task<(IReadOnlyList<PostRow> Posts, bool Truncated)> ReadPostsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid containerId, DateTimeOffset since, CancellationToken ct)
    {
        var posts = new List<PostRow>();
        await using var command = PostSql.Command(connection, transaction, """
            SELECT id,revision,base::text,field_prov::text,last_changed_at
            FROM harvest_posts
            WHERE container_id=@container AND merged_into IS NULL AND purged_at IS NULL AND state='live' AND last_changed_at >= @since
            ORDER BY last_changed_at DESC,id DESC LIMIT @limit
            """, ("container", containerId), ("since", since), ("limit", MaxPosts + 1));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var envelope = StoreJson.Read(reader.GetString(2), new PostEnvelope { Extraction = new ExtractionStamp { NormVersion = "unknown", RulesVersion = "unknown" } });
            var provenance = StoreJson.Read(reader.GetString(3), new Dictionary<string, Guid>());
            // The verdict reads the kind, the tags and the title's link only, so the bulky fields are dropped at once.
            var extras = envelope.Extras.TryGetValue(PostTagsConstants.ExtrasNamespace, out var postTags)
                ? new Dictionary<string, JsonElement> { [PostTagsConstants.ExtrasNamespace] = postTags }
                : new Dictionary<string, JsonElement>();
            posts.Add(new PostRow(reader.GetGuid(0), reader.GetInt32(1), envelope.Title, TitleEn(extras), reader.GetFieldValue<DateTimeOffset>(4),
                provenance.TryGetValue("title", out var titleLink) ? titleLink : null,
                envelope with { Text = null, Media = [], Links = [], Extras = extras }));
        }
        return posts.Count > MaxPosts ? (posts.Take(MaxPosts).ToList(), true) : (posts, false);
    }

    private static string? TitleEn(IReadOnlyDictionary<string, JsonElement> extras) =>
        extras.TryGetValue(PostTagsConstants.ExtrasNamespace, out var tags) && tags.ValueKind == JsonValueKind.Object
            && tags.TryGetProperty("titleEn", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;

    private static async Task<IReadOnlyDictionary<Guid, List<SelectorLink>>> ReadLinksAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid containerId, IReadOnlyList<PostRow> posts, IReadOnlyDictionary<Guid, SourceRow> sources, CancellationToken ct)
    {
        var links = new Dictionary<Guid, List<SelectorLink>>();
        if (posts.Count == 0) return links;
        await using var command = PostSql.Command(connection, transaction, """
            SELECT post_id,id,source_id,url FROM harvest_post_links
            WHERE container_id=@container AND post_id = ANY(@ids) AND retired_at IS NULL
            ORDER BY post_id,first_seen_at,id
            """, ("container", containerId), ("ids", posts.Select(post => post.Id).ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var postId = reader.GetGuid(0);
            var sourceId = reader.GetGuid(2);
            if (!links.TryGetValue(postId, out var list)) links[postId] = list = [];
            list.Add(new SelectorLink(reader.GetGuid(1), sourceId, sources.GetValueOrDefault(sourceId)?.GroupId, reader.GetString(3)));
        }
        return links;
    }

    private static Result<SubscriptionPreviewModel> Invalid(string message) => Result<SubscriptionPreviewModel>.Fail(AppErrorFactory.Validation(message));
    private static Result<SubscriptionPreviewModel> Missing(string message) => Result<SubscriptionPreviewModel>.Fail(AppErrorFactory.NotFound(message));

    private sealed record SourceRow(Guid? GroupId, string? Name);

    private sealed record PostRow(Guid Id, int Revision, string? Title, string? TitleEn, DateTimeOffset LastChangedAtUtc, Guid? PrimaryLinkId, PostEnvelope Envelope);
}
