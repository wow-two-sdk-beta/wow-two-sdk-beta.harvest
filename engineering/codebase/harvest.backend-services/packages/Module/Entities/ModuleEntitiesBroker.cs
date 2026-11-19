using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Reads entities, their years, posts and calendar, and the change feed over PostgreSQL, inside one container.</summary>
internal sealed class ModuleEntitiesBroker([FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource) : IEntitiesBroker
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EntitySummaryModel>>> SearchAsync(Guid containerId, string? query, string? entityType = null,
        string? domain = null, int limit = EntityRouteConstants.DefaultSearchLimit, CancellationToken cancellationToken = default)
    {
        string? text = EntityQueryRules.Text(query), type = EntityQueryRules.Normalize(entityType), key = EntityQueryRules.Normalize(domain);
        if (EntityQueryRules.Search(text, type, key, limit) is { } invalid)
            return Result<IReadOnlyList<EntitySummaryModel>>.Fail(invalid);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await EntityReadStore.ContainerExistsAsync(connection, containerId, cancellationToken))
            return Result<IReadOnlyList<EntitySummaryModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        string nameKey = text is null ? "" : EntityNames.Key(text);
        var hits = await EntityReadStore.SearchAsync(connection, containerId, nameKey.Length > 0 ? [nameKey] : [], text, type, key, limit,
            cancellationToken);
        var ids = hits.Select(hit => hit.Id).ToArray();
        var years = await EntityReadStore.YearsAsync(connection, ids, cancellationToken);
        var counts = await EntityReadStore.CountsAsync(connection, ids, cancellationToken);
        return Result<IReadOnlyList<EntitySummaryModel>>.Ok([.. hits.Select(hit => new EntitySummaryModel
        {
            Id = hit.Id, ContainerId = containerId, EntityType = hit.EntityType, Name = hit.Name, Aliases = hit.Aliases, PrimaryDomain = hit.PrimaryDomain,
            Status = hit.Status, Cadence = hit.Cadence, Score = hit.Score, Years = years[hit.Id], Predicted = counts[hit.Id].Predicted,
            Occurrences = counts[hit.Id].Occurrences, Posts = counts[hit.Id].Posts, Sources = counts[hit.Id].Sources, LastSeenAtUtc = hit.LastSeenAt,
        })]);
    }

    /// <inheritdoc />
    public async Task<Result<EntityTimelineModel>> GetAsync(Guid containerId, Guid entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (await EntityReadStore.ResolveAsync(connection, containerId, entityId, cancellationToken) is not { } entity)
            return Result<EntityTimelineModel>.Fail(await MissingAsync(connection, containerId, entityId, cancellationToken));

        var occurrences = (await EntityStore.ReadOccurrencesAsync(connection, null, entity.Id, cancellationToken)).Select(EntityEventLog.Model).ToList();
        var years = (await EntityReadStore.YearsAsync(connection, [entity.Id], cancellationToken))[entity.Id];
        var counts = (await EntityReadStore.CountsAsync(connection, [entity.Id], cancellationToken))[entity.Id];
        return Result<EntityTimelineModel>.Ok(new EntityTimelineModel
        {
            Id = entity.Id, RequestedId = entity.Id == entityId ? null : entityId, ContainerId = containerId, EntityType = entity.EntityType,
            Name = entity.Name, Aliases = entity.Aliases, PrimaryDomain = entity.PrimaryDomain, Status = entity.Status, Cadence = entity.Cadence,
            CadenceConfidence = entity.CadenceConfidence, FirstSeenAtUtc = entity.FirstSeenAt, LastSeenAtUtc = entity.LastSeenAt, Posts = counts.Posts,
            Sources = counts.Sources,
            Years = [.. years.Select(year => new EntityTimelineYearModel
            {
                Year = year.Year, Posts = year.Posts,
                Occurrences = [.. occurrences.Where(occurrence => occurrence.Starts.Year == year.Year).OrderBy(occurrence => occurrence.Starts)
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
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (await EntityReadStore.ResolveAsync(connection, containerId, entityId, cancellationToken) is not { } entity)
            return Result<CursorPage<EntityPostModel>>.Fail(await MissingAsync(connection, containerId, entityId, cancellationToken));
        if (!EntityCursors.TryPosts(entity.Id, cursor, out var afterKey, out var afterPost))
            return Result<CursorPage<EntityPostModel>>.Fail(EntityErrors.InvalidCursor());

        var rows = await EntityReadStore.PostsAsync(connection, entity.Id, year, kept, limit + 1, afterKey, afterPost, cancellationToken);
        bool hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        string next = rows.Count == 0 ? cursor ?? "" : EntityCursors.Posts(entity.Id, rows[^1].Post.PostId, rows[^1].SortKey);
        return Result<CursorPage<EntityPostModel>>.Ok(new CursorPage<EntityPostModel>
        {
            Data = [.. rows.Select(row => row.Post)], Next = next, HasMore = hasMore,
        });
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EntityCalendarEntryModel>>> CalendarAsync(Guid containerId, DateOnly from, DateOnly to, string? domain = null,
        string? entityType = null, CancellationToken cancellationToken = default)
    {
        string? type = EntityQueryRules.Normalize(entityType), key = EntityQueryRules.Normalize(domain);
        if (EntityQueryRules.Calendar(from, to, type, key) is { } invalid)
            return Result<IReadOnlyList<EntityCalendarEntryModel>>.Fail(invalid);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await EntityReadStore.ContainerExistsAsync(connection, containerId, cancellationToken))
            return Result<IReadOnlyList<EntityCalendarEntryModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        return Result<IReadOnlyList<EntityCalendarEntryModel>>.Ok(
            await EntityReadStore.CalendarAsync(connection, containerId, from, to, type, key, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<CursorPage<EntityEvent>>> PullEventsAsync(Guid containerId, string? cursor = null,
        int limit = EntityRouteConstants.DefaultEventsLimit, string? entityType = null, string? domain = null, CancellationToken cancellationToken = default)
    {
        string? type = EntityQueryRules.Normalize(entityType), key = EntityQueryRules.Normalize(domain);
        if (EntityQueryRules.Events(limit, type, key) is { } invalid)
            return Result<CursorPage<EntityEvent>>.Fail(invalid);
        if (!EntityCursors.TryFeed(containerId, cursor, out var transactionId, out var rowId))
            return Result<CursorPage<EntityEvent>>.Fail(EntityErrors.InvalidCursor());
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (!await EntityReadStore.ContainerExistsAsync(connection, containerId, cancellationToken))
            return Result<CursorPage<EntityEvent>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        var rows = await EntityReadStore.EventsAsync(connection, containerId, transactionId, rowId, limit + 1, type, key, cancellationToken);
        bool hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        string next = rows.Count == 0 ? cursor ?? EntityCursors.Feed(containerId, transactionId, rowId)
            : EntityCursors.Feed(containerId, rows[^1].TransactionId, rows[^1].RowId);
        return Result<CursorPage<EntityEvent>>.Ok(new CursorPage<EntityEvent> { Data = [.. rows.Select(row => row.Event)], Next = next, HasMore = hasMore });
    }

    /// <summary>Names what is missing: the container, else the entity in it.</summary>
    private static async Task<AppError> MissingAsync(NpgsqlConnection connection, Guid containerId, Guid entityId, CancellationToken ct) =>
        await EntityReadStore.ContainerExistsAsync(connection, containerId, ct)
            ? EntityErrors.EntityNotFound(entityId) : RegistrationErrors.ContainerNotFound(containerId);
}
