using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Posts;

/// <summary>Integrates the post store over the Harvest service's operator and read routes.</summary>
internal sealed class ApiPostBroker(HttpClient http) : IPostBroker
{
    /// <inheritdoc />
    public Task<Result<CaptureSaveResult>> SaveCaptureAsync(Guid containerId, Guid sourceId, Guid? runId, Capture capture, PostEnvelope envelope, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<CaptureSaveResult>(http, HttpMethod.Post, PostRouteConstants.Posts + "/captures",
            new SaveCaptureRequest { ContainerId = containerId, SourceId = sourceId, RunId = runId, Capture = capture, Envelope = envelope }, cancellationToken);
    /// <inheritdoc />
    public Task<Result<PostModel>> ObserveLinkAsync(Guid containerId, Guid sourceId, LinkObservation observation, Guid? runId = null, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<PostModel>(http, HttpMethod.Post, PostRouteConstants.Posts + "/observations",
            new LinkObservationRequest { ContainerId = containerId, SourceId = sourceId, RunId = runId, Observation = observation }, cancellationToken);
    /// <inheritdoc />
    public Task<Result<PostModel>> GetPostAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<PostModel>(http, HttpMethod.Get, $"{PostRouteConstants.Posts}/{postId}?containerId={containerId}", null, cancellationToken);
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PostModel>>> ListPostsAsync(Guid containerId, int limit = 100, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<PostModel>>(http, HttpMethod.Get, $"{PostRouteConstants.Posts}?containerId={containerId}&limit={limit}", null, cancellationToken);
    /// <inheritdoc />
    public Task<Result<PostMergeModel>> MergeAsync(Guid containerId, Guid survivorId, Guid loserId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<PostMergeModel>(http, HttpMethod.Post, PostRouteConstants.Merges,
            new { ContainerId = containerId, SurvivorId = survivorId, LoserId = loserId }, cancellationToken);
    /// <inheritdoc />
    public Task<Result<PostMergeModel>> UnmergeAsync(Guid containerId, Guid mergeId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<PostMergeModel>(http, HttpMethod.Delete, $"{PostRouteConstants.Merges}/{mergeId}?containerId={containerId}", null, cancellationToken);
}
