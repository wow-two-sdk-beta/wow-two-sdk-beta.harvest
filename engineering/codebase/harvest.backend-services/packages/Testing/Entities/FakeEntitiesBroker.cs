using System.Globalization;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Entities;

/// <summary>Implements the entity reads in memory for consumer tests, by the module's rules, over the registry and post
/// fakes: <see cref="Seed"/> stands in for resolution, and <see cref="Append"/> adds a change event of a test's own.</summary>
/// <remarks>Search scores by shared words of the name and aliases, where the module uses trigram similarity; both keep a
/// hit at 0.35 or more.</remarks>
public sealed class FakeEntitiesBroker(IRegistrationBroker registry, IPostBroker posts) : IEntitiesBroker
{
    private const decimal MinScore = 0.35m;
    private readonly object gate = new();
    private readonly Dictionary<Guid, EntitySeed> entities = [];
    private readonly List<(long Sequence, EntityEvent Event)> log = [];
    private long sequence;

    /// <summary>Stores an entity as resolution would have left it and appends its creation events.</summary>
    /// <param name="seed">The entity, its occurrences and its bound posts.</param>
    public void Seed(EntitySeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        lock (gate)
        {
            entities[seed.Id] = seed;
            Append(Event(seed, EntityEventTypes.EntityCreated, null));
            foreach (var occurrence in seed.Occurrences.OrderBy(occurrence => occurrence.Starts).ThenBy(occurrence => occurrence.Id))
                Append(Event(seed, occurrence.Predicted ? EntityEventTypes.OccurrencePredicted : EntityEventTypes.OccurrenceAdded, occurrence));
        }
    }

    /// <summary>Appends a change event to its container's feed.</summary>
    /// <param name="change">The event.</param>
    public void Append(EntityEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (gate)
            log.Add((++sequence, change));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EntitySummaryModel>>> SearchAsync(Guid containerId, string? query, string? entityType = null, string? domain = null,
        int limit = EntityRouteConstants.DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        string? text = EntityQueryRules.Text(query), type = EntityQueryRules.Normalize(entityType), key = EntityQueryRules.Normalize(domain);
        if (EntityQueryRules.Search(text, type, key, limit) is { } invalid)
            return Result<IReadOnlyList<EntitySummaryModel>>.Fail(invalid);
        if (!await ContainerExistsAsync(containerId, cancellationToken))
            return Result<IReadOnlyList<EntitySummaryModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        var hits = Snapshot(containerId)
            .Where(entity => (type is null || entity.EntityType == type) && EntityQueryRules.AtOrUnder(entity.PrimaryDomain, key))
            .Select(entity => (Entity: entity, Score: Score(text, entity)))
            .Where(hit => hit.Score is null or >= MinScore)
            .OrderByDescending(hit => hit.Score ?? -1).ThenByDescending(hit => hit.Entity.SeenAtUtc).ThenBy(hit => hit.Entity.Id.ToString("N"), StringComparer.Ordinal)
            .Take(limit).ToList();
        var summaries = new List<EntitySummaryModel>();
        foreach (var (entity, score) in hits)
        {
            var bound = await PostsAsync(entity, cancellationToken);
            summaries.Add(new EntitySummaryModel
            {
                Id = entity.Id, ContainerId = containerId, EntityType = entity.EntityType, Name = entity.Name, Aliases = entity.Aliases,
                PrimaryDomain = entity.PrimaryDomain, Status = entity.Status, Cadence = entity.Cadence, Score = score, Years = Years(entity, bound),
                Predicted = entity.Occurrences.Where(occurrence => occurrence.Predicted).Select(occurrence => (DateOnly?)occurrence.Starts).Min(),
                Occurrences = entity.Occurrences.Count(occurrence => !occurrence.Predicted), Posts = bound.Count,
                Sources = bound.SelectMany(post => post.Sources).Distinct().Count(), LastSeenAtUtc = entity.SeenAtUtc,
            });
        }
        return Result<IReadOnlyList<EntitySummaryModel>>.Ok(summaries);
    }

    /// <inheritdoc />
    public async Task<Result<EntityTimelineModel>> GetAsync(Guid containerId, Guid entityId, CancellationToken cancellationToken = default)
    {
        var found = await FindAsync(containerId, entityId, cancellationToken);
        if (found is not Result<EntitySeed>.Success { Value: var entity })
            return Result<EntityTimelineModel>.Fail(((Result<EntitySeed>.Failure)found).Error);
        var bound = await PostsAsync(entity, cancellationToken);
        return Result<EntityTimelineModel>.Ok(new EntityTimelineModel
        {
            Id = entity.Id, ContainerId = containerId, EntityType = entity.EntityType, Name = entity.Name, Aliases = entity.Aliases,
            PrimaryDomain = entity.PrimaryDomain, Status = entity.Status, Cadence = entity.Cadence, FirstSeenAtUtc = entity.SeenAtUtc,
            LastSeenAtUtc = entity.SeenAtUtc, Posts = bound.Count, Sources = bound.SelectMany(post => post.Sources).Distinct().Count(),
            Years = [.. Years(entity, bound).Select(year => new EntityTimelineYearModel
            {
                Year = year.Year, Posts = year.Posts,
                Occurrences = [.. entity.Occurrences.Where(occurrence => occurrence.Starts.Year == year.Year).OrderBy(occurrence => occurrence.Starts)
                    .ThenBy(occurrence => occurrence.Id)],
            })],
        });
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<EntityPostModel>>> ListPostsAsync(Guid containerId, Guid entityId, int? year = null, string? role = null,
        int limit = EntityRouteConstants.DefaultPostsLimit, string? cursor = null, CancellationToken cancellationToken = default)
    {
        string? kept = EntityQueryRules.Normalize(role);
        if (EntityQueryRules.Posts(year, kept, limit) is { } invalid)
            return Result<CursorPage<EntityPostModel>>.Fail(invalid);
        var found = await FindAsync(containerId, entityId, cancellationToken);
        if (found is not Result<EntitySeed>.Success { Value: var entity })
            return Result<CursorPage<EntityPostModel>>.Fail(((Result<EntitySeed>.Failure)found).Error);
        if (!TryPostsCursor(entity.Id, cursor, out var afterKey, out var afterPost))
            return Result<CursorPage<EntityPostModel>>.Fail(EntityErrors.InvalidCursor());

        var ordered = (await PostsAsync(entity, cancellationToken))
            .Where(post => (year is null || post.Model.Year == year) && (kept is null || post.Model.Role == kept))
            .OrderByDescending(post => post.SortKey, StringComparer.Ordinal).ThenByDescending(post => post.Model.PostId.ToString("N"), StringComparer.Ordinal)
            .Where(post => afterKey is null || Before(post, afterKey, afterPost))
            .ToList();
        var page = ordered.Take(limit).ToList();
        string next = page.Count == 0 ? cursor ?? "" : PostsCursor(entity.Id, page[^1].Model.PostId, page[^1].SortKey);
        return Result<CursorPage<EntityPostModel>>.Ok(new CursorPage<EntityPostModel>
        {
            Data = [.. page.Select(post => post.Model)], Next = next, HasMore = ordered.Count > limit,
        });
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EntityCalendarEntryModel>>> CalendarAsync(Guid containerId, DateOnly from, DateOnly to, string? domain = null,
        string? entityType = null, CancellationToken cancellationToken = default)
    {
        string? type = EntityQueryRules.Normalize(entityType), key = EntityQueryRules.Normalize(domain);
        if (EntityQueryRules.Calendar(from, to, type, key) is { } invalid)
            return Result<IReadOnlyList<EntityCalendarEntryModel>>.Fail(invalid);
        if (!await ContainerExistsAsync(containerId, cancellationToken))
            return Result<IReadOnlyList<EntityCalendarEntryModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        var entries = new List<EntityCalendarEntryModel>();
        foreach (var entity in Snapshot(containerId).Where(entity => (type is null || entity.EntityType == type) && EntityQueryRules.AtOrUnder(entity.PrimaryDomain, key)))
        {
            var bound = await PostsAsync(entity, cancellationToken);
            foreach (var occurrence in entity.Occurrences.Where(occurrence => occurrence.Starts <= to && Last(occurrence) >= from))
                entries.Add(new EntityCalendarEntryModel
                {
                    EntityId = entity.Id, EntityType = entity.EntityType, Name = entity.Name, PrimaryDomain = entity.PrimaryDomain, Cadence = entity.Cadence,
                    Occurrence = occurrence, Posts = bound.Count(post => post.Model.OccurrenceId == occurrence.Id),
                });
        }
        return Result<IReadOnlyList<EntityCalendarEntryModel>>.Ok([.. entries.OrderBy(entry => entry.Occurrence.Starts)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal).ThenBy(entry => entry.Occurrence.Id.ToString("N"), StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<EntityEvent>>> PullEventsAsync(Guid containerId, string? cursor = null, int limit = EntityRouteConstants.DefaultEventsLimit,
        string? entityType = null, string? domain = null, CancellationToken cancellationToken = default)
    {
        string? type = EntityQueryRules.Normalize(entityType), key = EntityQueryRules.Normalize(domain);
        if (EntityQueryRules.Events(limit, type, key) is { } invalid)
            return Result<CursorPage<EntityEvent>>.Fail(invalid);
        if (!TryFeedCursor(containerId, cursor, out var after))
            return Result<CursorPage<EntityEvent>>.Fail(EntityErrors.InvalidCursor());
        if (!await ContainerExistsAsync(containerId, cancellationToken))
            return Result<CursorPage<EntityEvent>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        List<(long Sequence, EntityEvent Event)> rows;
        lock (gate)
            rows = [.. log.Where(row => row.Sequence > after && row.Event.ContainerId == containerId && (type is null || row.Event.EntityType == type)
                && EntityQueryRules.AtOrUnder(row.Event.PrimaryDomain, key))];
        var page = rows.Take(limit).ToList();
        string next = page.Count == 0 ? cursor ?? FeedCursor(containerId, after) : FeedCursor(containerId, page[^1].Sequence);
        return Result<CursorPage<EntityEvent>>.Ok(new CursorPage<EntityEvent> { Data = [.. page.Select(row => row.Event)], Next = next, HasMore = rows.Count > limit });
    }

    private async Task<bool> ContainerExistsAsync(Guid containerId, CancellationToken ct) =>
        await registry.GetContainerAsync(containerId, ct) is Result<ContainerModel>.Success;

    private async Task<Result<EntitySeed>> FindAsync(Guid containerId, Guid entityId, CancellationToken ct)
    {
        if (!await ContainerExistsAsync(containerId, ct))
            return Result<EntitySeed>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        lock (gate)
            return entities.TryGetValue(entityId, out var entity) && entity.ContainerId == containerId
                ? Result<EntitySeed>.Ok(entity) : Result<EntitySeed>.Fail(EntityErrors.EntityNotFound(entityId));
    }

    private List<EntitySeed> Snapshot(Guid containerId)
    {
        lock (gate)
            return [.. entities.Values.Where(entity => entity.ContainerId == containerId)];
    }

    /// <summary>Reads the entity's surviving bound posts as the module shows them, with each one's sort key and sources.</summary>
    private async Task<List<BoundPost>> PostsAsync(EntitySeed entity, CancellationToken ct)
    {
        var bound = new List<BoundPost>();
        foreach (var relation in entity.Relations)
        {
            if (await posts.GetPostAsync(entity.ContainerId, relation.PostId, ct) is not Result<PostModel>.Success { Value: var post } || post.MergedInto is not null)
                continue;
            var link = post.FieldSources.TryGetValue("title", out var titleLink) ? post.Links.FirstOrDefault(candidate => candidate.Id == titleLink) : null;
            link ??= post.Links.FirstOrDefault();
            string? source = link is not null && await registry.GetSourceAsync(link.SourceId, ct) is Result<SourceModel>.Success { Value: var found }
                ? found.Name ?? found.Handle : null;
            string? posted = post.Base.PostedAt?.Value is { Length: > 0 } stated ? stated : null;
            var occurrence = entity.Occurrences.FirstOrDefault(candidate => candidate.Id == relation.OccurrenceId);
            int year = occurrence?.Starts.Year
                ?? (posted is { Length: >= 4 } && posted[..4].All(char.IsAsciiDigit) ? int.Parse(posted[..4], CultureInfo.InvariantCulture)
                    : post.FirstSeenAtUtc.UtcDateTime.Year);
            bound.Add(new BoundPost(new EntityPostModel
            {
                PostId = post.Id, Role = relation.Role, Changes = relation.Changes, OccurrenceId = relation.OccurrenceId, Year = year, Title = post.Base.Title,
                Link = link?.Url, SourceId = link?.SourceId, Source = source, PostedAt = posted, FirstSeenAtUtc = post.FirstSeenAtUtc,
                BoundAtUtc = relation.BoundAtUtc,
            }, posted ?? post.FirstSeenAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                [.. post.Links.Select(candidate => candidate.SourceId).Distinct()]));
        }
        return bound;
    }

    private static List<EntityYearSummaryModel> Years(EntitySeed entity, List<BoundPost> bound) =>
    [
        .. entity.Occurrences.Select(occurrence => occurrence.Starts.Year).Concat(bound.Select(post => post.Model.Year)).Distinct().Order()
            .Select(year => new EntityYearSummaryModel
            {
                Year = year, Occurrences = entity.Occurrences.Count(occurrence => occurrence.Starts.Year == year && !occurrence.Predicted),
                Predicted = entity.Occurrences.Any(occurrence => occurrence.Starts.Year == year && occurrence.Predicted),
                Posts = bound.Count(post => post.Model.Year == year),
            }),
    ];

    private static decimal? Score(string? text, EntitySeed entity)
    {
        if (text is null)
            return null;
        var wanted = Words(text);
        if (wanted.Count == 0)
            return 0;
        decimal best = 0;
        foreach (var name in entity.Aliases.Prepend(entity.Name))
        {
            var have = Words(name);
            best = Math.Max(best, wanted.Count(have.Contains) / (decimal)wanted.Count);
        }
        return Math.Round(best, 3);
    }

    private static HashSet<string> Words(string text)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var word = new StringBuilder();
        foreach (char character in text.ToLowerInvariant().Append(' '))
        {
            if (char.IsLetterOrDigit(character))
            {
                word.Append(character);
                continue;
            }
            if (word.Length > 0)
                words.Add(word.ToString());
            word.Clear();
        }
        return words;
    }

    private static DateOnly Last(OccurrenceModel occurrence) => occurrence.Ends ?? occurrence.Precision switch
    {
        "year" => new DateOnly(occurrence.Starts.Year, 12, 31),
        "month" => new DateOnly(occurrence.Starts.Year, occurrence.Starts.Month, DateTime.DaysInMonth(occurrence.Starts.Year, occurrence.Starts.Month)),
        _ => occurrence.Starts,
    };

    private static bool Before(BoundPost post, string key, Guid postId)
    {
        int order = string.CompareOrdinal(post.SortKey, key);
        return order < 0 || order == 0 && string.CompareOrdinal(post.Model.PostId.ToString("N"), postId.ToString("N")) < 0;
    }

    private static EntityEvent Event(EntitySeed entity, string type, OccurrenceModel? occurrence) => new()
    {
        EventId = Guid.CreateVersion7(entity.SeenAtUtc), Type = type, OccurredAtUtc = entity.SeenAtUtc, ContainerId = entity.ContainerId,
        EntityId = entity.Id, EntityType = entity.EntityType, Name = entity.Name, PrimaryDomain = entity.PrimaryDomain, Status = entity.Status,
        Cadence = entity.Cadence, OccurrenceId = occurrence?.Id, Occurrence = occurrence,
    };

    private static string FeedCursor(Guid containerId, long after) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{containerId:N}:{after.ToString(CultureInfo.InvariantCulture)}"));

    private static bool TryFeedCursor(Guid containerId, string? cursor, out long after)
    {
        after = 0;
        if (string.IsNullOrEmpty(cursor))
            return true;
        var parts = Decode(cursor)?.Split(':');
        return parts is { Length: 2 } && Guid.TryParseExact(parts[0], "N", out var owner) && owner == containerId
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out after);
    }

    private static string PostsCursor(Guid entityId, Guid postId, string key) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{entityId:N}:{postId:N}:{key}"));

    private static bool TryPostsCursor(Guid entityId, string? cursor, out string? key, out Guid postId)
    {
        key = null;
        postId = Guid.Empty;
        if (string.IsNullOrEmpty(cursor))
            return true;
        var parts = Decode(cursor)?.Split(':', 3);
        if (parts is not { Length: 3 } || !Guid.TryParseExact(parts[0], "N", out var owner) || owner != entityId
            || !Guid.TryParseExact(parts[1], "N", out postId) || parts[2].Length == 0)
            return false;
        key = parts[2];
        return true;
    }

    private static string? Decode(string cursor)
    {
        if (cursor.Length > 512)
            return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed record BoundPost(EntityPostModel Model, string SortKey, IReadOnlyList<Guid> Sources);
}
