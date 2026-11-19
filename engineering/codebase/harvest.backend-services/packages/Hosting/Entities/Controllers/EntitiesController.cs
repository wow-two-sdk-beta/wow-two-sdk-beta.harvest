using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Entities.Controllers;

/// <summary>Exposes the entity reads and change feed inside the containers a caller may see, under <c>posts:read</c>:
/// entities are what posts are about, and every read carries no more of a post than its title and link.</summary>
[ApiController]
[Route(EntityRouteConstants.Entities)]
public sealed class EntitiesController(IEntitiesBroker entities, IRegistrationBroker registry, CallerResolver callers) : ControllerBase
{
    /// <summary>Finds a visible container's entities by name, type and domain, with the years each has something for.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="query">The name to search for; blank lists by filters only.</param>
    /// <param name="type">The entity type, <c>event</c> or <c>listing</c>.</param>
    /// <param name="domain">The taxonomy key the primary domain equals or sits under.</param>
    /// <param name="limit">The most entities, 1 to 100.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EntitySummaryModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId,
        [FromQuery(Name = EntityRouteConstants.QueryParameter)] string? query, [FromQuery(Name = EntityRouteConstants.TypeParameter)] string? type,
        [FromQuery(Name = EntityRouteConstants.DomainParameter)] string? domain,
        [FromQuery(Name = EntityRouteConstants.LimitParameter)] int limit = EntityRouteConstants.DefaultSearchLimit, CancellationToken ct = default)
    {
        if (await AccessErrorAsync(containerId, ct) is { } error)
            return this.ToProblem(error);
        return (await entities.SearchAsync(containerId, query, type, domain, limit, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<EntitySummaryModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads a visible entity with its occurrences by year.</summary>
    /// <param name="entityId">The entity.</param>
    /// <param name="containerId">The container.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{entityId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<EntityTimelineModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid entityId, [FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId,
        CancellationToken ct)
    {
        if (await AccessErrorAsync(containerId, ct) is { } error)
            return this.ToProblem(error);
        return (await entities.GetAsync(containerId, entityId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<EntityTimelineModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Lists a visible entity's posts from every source, newest posted first, optionally of one year and role.</summary>
    /// <param name="entityId">The entity.</param>
    /// <param name="containerId">The container.</param>
    /// <param name="year">The year the posts are about.</param>
    /// <param name="role">The relation role.</param>
    /// <param name="limit">The page size, 1 to 200.</param>
    /// <param name="cursor">The cursor a previous page returned.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{entityId:guid}/" + EntityRouteConstants.Posts)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<CursorPage<EntityPostModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Posts(Guid entityId, [FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId,
        [FromQuery(Name = EntityRouteConstants.YearParameter)] int? year, [FromQuery(Name = EntityRouteConstants.RoleParameter)] string? role,
        [FromQuery(Name = EntityRouteConstants.LimitParameter)] int limit = EntityRouteConstants.DefaultPostsLimit,
        [FromQuery(Name = EntityRouteConstants.CursorParameter)] string? cursor = null, CancellationToken ct = default)
    {
        if (await AccessErrorAsync(containerId, ct) is { } error)
            return this.ToProblem(error);
        return (await entities.ListPostsAsync(containerId, entityId, year, role, limit, cursor, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<CursorPage<EntityPostModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Lists a visible container's occurrences that overlap a window, predicted ones marked.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="from">The window's first day, <c>yyyy-MM-dd</c>.</param>
    /// <param name="to">The window's last day, at most 366 days later.</param>
    /// <param name="domain">The taxonomy key the primary domain equals or sits under.</param>
    /// <param name="type">The entity type.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet(EntityRouteConstants.Calendar)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<EntityCalendarEntryModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Calendar([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId,
        [FromQuery(Name = EntityRouteConstants.FromParameter)] DateOnly? from, [FromQuery(Name = EntityRouteConstants.ToParameter)] DateOnly? to,
        [FromQuery(Name = EntityRouteConstants.DomainParameter)] string? domain, [FromQuery(Name = EntityRouteConstants.TypeParameter)] string? type,
        CancellationToken ct)
    {
        if (await AccessErrorAsync(containerId, ct) is { } error)
            return this.ToProblem(error);
        if (from is not { } first || to is not { } last)
            return this.ToProblem(EntityErrors.Invalid("The calendar takes from and to, as yyyy-MM-dd."));
        return (await entities.CalendarAsync(containerId, first, last, domain, type, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<EntityCalendarEntryModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads a snapshot-stable page of a visible container's entity change feed, with a cursor for a later poll.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="cursor">The cursor a previous page returned.</param>
    /// <param name="limit">The page size, 1 to 500.</param>
    /// <param name="type">The entity type.</param>
    /// <param name="domain">The taxonomy key the entity's primary domain equals or sits under.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet(EntityRouteConstants.Events)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<CursorPage<EntityEvent>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Events([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId,
        [FromQuery(Name = EntityRouteConstants.CursorParameter)] string? cursor,
        [FromQuery(Name = EntityRouteConstants.LimitParameter)] int limit = EntityRouteConstants.DefaultEventsLimit,
        [FromQuery(Name = EntityRouteConstants.TypeParameter)] string? type = null,
        [FromQuery(Name = EntityRouteConstants.DomainParameter)] string? domain = null, CancellationToken ct = default)
    {
        if (await AccessErrorAsync(containerId, ct) is { } error)
            return this.ToProblem(error);
        return (await entities.PullEventsAsync(containerId, cursor, limit, type, domain, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<CursorPage<EntityEvent>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Returns why the caller may not read the container, as post reads do: unknown or another tenant's private
    /// container is 404.</summary>
    private async Task<AppError?> AccessErrorAsync(Guid containerId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return AppErrorFactory.Unauthorized();
        if (await registry.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container } || !caller.CanRead(container))
            return RegistrationErrors.ContainerNotFound(containerId);
        return null;
    }
}
