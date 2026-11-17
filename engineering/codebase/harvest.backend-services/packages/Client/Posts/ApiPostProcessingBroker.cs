using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Posts;

/// <summary>Reads capture processing evidence through the authenticated service.</summary>
internal sealed class ApiPostProcessingBroker(HttpClient http) : IPostProcessingBroker
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PostProcessingModel>>> ListAsync(Guid containerId, Guid postId, int limit = 50,
        Guid? sourceId = null, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<PostProcessingModel>>(http, HttpMethod.Get,
            $"{PostRouteConstants.Posts}/{postId}/processing?containerId={containerId}&limit={limit}"
            + (sourceId is { } source ? $"&sourceId={source}" : ""), null, cancellationToken);
}
