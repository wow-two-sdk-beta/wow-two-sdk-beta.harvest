using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Sources;

/// <summary>Reads the console's Sources area over the running Harvest service.</summary>
internal sealed class ApiSourceOverviewBroker(HttpClient http) : ISourceOverviewBroker
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SourceOverviewModel>>> ListOverviewAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<SourceOverviewModel>>(http, HttpMethod.Get,
            $"{SourceRouteConstants.Overview}?{RegistrationRouteConstants.ContainerIdParameter}={containerId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SourcePostModel>>> ListSourcePostsAsync(Guid sourceId, int limit = SourceConstants.DefaultPostsLimit, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<SourcePostModel>>(http, HttpMethod.Get,
            $"{RegistrationRouteConstants.Sources}/{sourceId}/{SourceRouteConstants.Posts}?{SourceRouteConstants.LimitParameter}={limit}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PostRevisionModel>>> ListPostRevisionsAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<PostRevisionModel>>(http, HttpMethod.Get,
            $"{PostRouteConstants.Posts}/{postId}/{SourceRouteConstants.Revisions}?{RegistrationRouteConstants.ContainerIdParameter}={containerId}",
            null, cancellationToken);
}
