using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Reads the entities posts are about — events, one record across their years, and listings, one record across
/// authors and sites — and their change feed, inside one container.</summary>
/// <remarks>Read-only over the entity layer's tables; <c>EntitiesBrokerConformance</c> runs against every form. Every
/// read is scoped to a container: an entity of another container is <c>NotFound</c>, as a post read is. Failures are
/// <c>Validation</c> for a malformed request and <c>NotFound</c> for an unknown container or entity; the HTTP form adds
/// <c>Unauthorized</c>, <c>Forbidden</c> and <c>ExternalUnavailable</c>. No read returns post text beyond titles.</remarks>
public interface IEntitiesBroker
{
    /// <summary>Finds entities by name or alias, in any of the sources' languages, with the years they have something
    /// for; without a query, lists the newest entities the filters keep.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="query">The name to search for, 1 to 300 characters; null or blank lists by filters only.</param>
    /// <param name="entityType">The entity type to keep, one of <see cref="EntityTypes"/>; both when null.</param>
    /// <param name="domain">The taxonomy key the primary domain must equal or sit under, such as <c>style-fashion</c>.</param>
    /// <param name="limit">The most entities to return, 1 to <see cref="EntityRouteConstants.MaxSearchLimit"/>.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The entities, best match first, else newest first.</returns>
    Task<Result<IReadOnlyList<EntitySummaryModel>>> SearchAsync(Guid containerId, string? query, string? entityType = null, string? domain = null,
        int limit = EntityRouteConstants.DefaultSearchLimit, CancellationToken cancellationToken = default);

    /// <summary>Reads an entity with its occurrences by year; an entity merged away reads as the one it was merged into.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="entityId">The entity.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The entity; <c>NotFound</c> when the container holds no such entity.</returns>
    Task<Result<EntityTimelineModel>> GetAsync(Guid containerId, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Lists the posts bound to an entity, newest posted first, from every source; with a year, only that
    /// year's (<see cref="EntityPostModel.Year"/>).</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="entityId">The entity; one merged away lists the posts of the one it was merged into.</param>
    /// <param name="year">The year to keep.</param>
    /// <param name="role">The relation role to keep, one of <see cref="EntityRoles"/>.</param>
    /// <param name="limit">The most posts per page, 1 to <see cref="EntityRouteConstants.MaxPostsLimit"/>.</param>
    /// <param name="cursor">The opaque cursor a previous page returned; the first page when null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>One page; <c>Validation</c> for a cursor of another entity.</returns>
    Task<Result<CursorPage<EntityPostModel>>> ListPostsAsync(Guid containerId, Guid entityId, int? year = null, string? role = null,
        int limit = EntityRouteConstants.DefaultPostsLimit, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the occurrences that overlap a window, predicted ones marked, earliest first.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="from">The window's first day.</param>
    /// <param name="to">The window's last day, at most <see cref="EntityRouteConstants.MaxCalendarDays"/> days after
    /// <paramref name="from"/>.</param>
    /// <param name="domain">The taxonomy key the primary domain must equal or sit under.</param>
    /// <param name="entityType">The entity type to keep; only events have occurrences.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<IReadOnlyList<EntityCalendarEntryModel>>> CalendarAsync(Guid containerId, DateOnly from, DateOnly to, string? domain = null,
        string? entityType = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the container's entity change feed: committed events below the transaction snapshot horizon, in log
    /// order. An older cursor replays the same events; omitting it starts over.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="cursor">The opaque cursor a previous page returned.</param>
    /// <param name="limit">The most events per page, 1 to <see cref="EntityRouteConstants.MaxEventsLimit"/>.</param>
    /// <param name="entityType">The entity type to keep.</param>
    /// <param name="domain">The taxonomy key the entity's primary domain must equal or sit under.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>One page; <c>Validation</c> for a malformed cursor or one of another container.</returns>
    Task<Result<CursorPage<EntityEvent>>> PullEventsAsync(Guid containerId, string? cursor = null, int limit = EntityRouteConstants.DefaultEventsLimit,
        string? entityType = null, string? domain = null, CancellationToken cancellationToken = default);
}
