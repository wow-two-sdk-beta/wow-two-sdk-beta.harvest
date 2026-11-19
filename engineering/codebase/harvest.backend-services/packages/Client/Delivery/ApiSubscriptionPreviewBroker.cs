using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;

/// <summary>Integrates selector previews over the Harvest service.</summary>
internal sealed class ApiSubscriptionPreviewBroker(HttpClient http) : ISubscriptionPreviewBroker
{
    /// <inheritdoc />
    public Task<Result<SubscriptionPreviewModel>> PreviewAsync(SubscriptionPreviewRequest request, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<SubscriptionPreviewModel>(http, HttpMethod.Post, DeliveryRouteConstants.SubscriptionPreview, request, cancellationToken);
}
