using System.Globalization;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Entities;

/// <summary>Reads entities and their change feed over the running Harvest service's <c>api/entities</c> routes.</summary>
internal sealed class ApiEntitiesBroker(HttpClient http) : IEntitiesBroker
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<EntitySummaryModel>>> SearchAsync(Guid containerId, string? query, string? entityType = null, string? domain = null,
        int limit = EntityRouteConstants.DefaultSearchLimit, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<EntitySummaryModel>>(http, HttpMethod.Get, Route(EntityRouteConstants.Entities, containerId,
            (EntityRouteConstants.QueryParameter, query), (EntityRouteConstants.TypeParameter, entityType), (EntityRouteConstants.DomainParameter, domain),
            (EntityRouteConstants.LimitParameter, Number(limit))), null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<EntityTimelineModel>> GetAsync(Guid containerId, Guid entityId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<EntityTimelineModel>(http, HttpMethod.Get, Route($"{EntityRouteConstants.Entities}/{entityId:D}", containerId), null,
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<CursorPage<EntityPostModel>>> ListPostsAsync(Guid containerId, Guid entityId, int? year = null, string? role = null,
        int limit = EntityRouteConstants.DefaultPostsLimit, string? cursor = null, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<CursorPage<EntityPostModel>>(http, HttpMethod.Get,
            Route($"{EntityRouteConstants.Entities}/{entityId:D}/{EntityRouteConstants.Posts}", containerId,
                (EntityRouteConstants.YearParameter, year is { } value ? Number(value) : null), (EntityRouteConstants.RoleParameter, role),
                (EntityRouteConstants.LimitParameter, Number(limit)), (EntityRouteConstants.CursorParameter, cursor)), null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<EntityCalendarEntryModel>>> CalendarAsync(Guid containerId, DateOnly from, DateOnly to, string? domain = null,
        string? entityType = null, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<EntityCalendarEntryModel>>(http, HttpMethod.Get,
            Route($"{EntityRouteConstants.Entities}/{EntityRouteConstants.Calendar}", containerId, (EntityRouteConstants.FromParameter, Day(from)),
                (EntityRouteConstants.ToParameter, Day(to)), (EntityRouteConstants.DomainParameter, domain), (EntityRouteConstants.TypeParameter, entityType)),
            null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<CursorPage<EntityEvent>>> PullEventsAsync(Guid containerId, string? cursor = null, int limit = EntityRouteConstants.DefaultEventsLimit,
        string? entityType = null, string? domain = null, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<CursorPage<EntityEvent>>(http, HttpMethod.Get,
            Route($"{EntityRouteConstants.Entities}/{EntityRouteConstants.Events}", containerId, (EntityRouteConstants.CursorParameter, cursor),
                (EntityRouteConstants.LimitParameter, Number(limit)), (EntityRouteConstants.TypeParameter, entityType),
                (EntityRouteConstants.DomainParameter, domain)), null, cancellationToken);

    /// <summary>Builds a relative route with the container and every given parameter, escaped; a null value is left out.</summary>
    internal static string Route(string path, Guid containerId, params (string Name, string? Value)[] parameters)
    {
        var route = new StringBuilder(path).Append('?').Append(RegistrationRouteConstants.ContainerIdParameter).Append('=').Append(containerId.ToString("D"));
        foreach (var (name, value) in parameters)
            if (value is not null)
                route.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        return route.ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Day(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
