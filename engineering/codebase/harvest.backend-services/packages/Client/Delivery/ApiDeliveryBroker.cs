using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;

/// <summary>Integrates pull subscriptions over the Harvest service.</summary>
internal sealed class ApiDeliveryBroker(HttpClient http) : IDeliveryBroker
{
    /// <inheritdoc />
    public Task<Result<SubscriptionModel>> SubscribeAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<SubscriptionModel>(http, HttpMethod.Post, DeliveryRouteConstants.Subscriptions, declaration, cancellationToken);
    /// <inheritdoc />
    public Task<Result<SubscriptionModel>> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<SubscriptionModel>(http, HttpMethod.Get, $"{DeliveryRouteConstants.Subscriptions}/{subscriptionId}", null, cancellationToken);
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SubscriptionModel>>> ListSubscriptionsAsync(Guid? tenantId = null, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<SubscriptionModel>>(http, HttpMethod.Get,
            DeliveryRouteConstants.Subscriptions + (tenantId is { } id ? $"?tenantId={id}" : string.Empty), null, cancellationToken);
    /// <inheritdoc />
    public Task<Result<CursorPage<DeliveryEvent>>> PullAsync(Guid subscriptionId, string? cursor = null, int limit = 100, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<CursorPage<DeliveryEvent>>(http, HttpMethod.Get,
            $"{DeliveryRouteConstants.Subscriptions}/{subscriptionId}/events?limit={limit}" + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}"), null, cancellationToken);
}
