using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Stores captures, resolves their posts and records reversible link membership changes.</summary>
public interface IPostBroker
{
    /// <summary>Stores changed content and its extraction atomically with subscription events.</summary>
    Task<Result<CaptureSaveResult>> SaveCaptureAsync(Guid containerId, Guid sourceId, Guid? runId, Capture capture, PostEnvelope envelope, CancellationToken cancellationToken = default);
    /// <summary>Applies existence evidence to a source-local link and rolls up the post state.</summary>
    Task<Result<PostModel>> ObserveLinkAsync(Guid containerId, Guid sourceId, LinkObservation observation, Guid? runId = null, CancellationToken cancellationToken = default);
    /// <summary>Reads a post, restricted to its owning container.</summary>
    Task<Result<PostModel>> GetPostAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default);
    /// <summary>Lists the container's surviving posts, most recently changed first.</summary>
    Task<Result<IReadOnlyList<PostModel>>> ListPostsAsync(Guid containerId, int limit = 100, CancellationToken cancellationToken = default);
    /// <summary>Moves one post's links to another and records the exact moved set.</summary>
    Task<Result<PostMergeModel>> MergeAsync(Guid containerId, Guid survivorId, Guid loserId, CancellationToken cancellationToken = default);
    /// <summary>Restores a merge's link membership and keeps the posts apart during future deduplication.</summary>
    Task<Result<PostMergeModel>> UnmergeAsync(Guid containerId, Guid mergeId, CancellationToken cancellationToken = default);
}
