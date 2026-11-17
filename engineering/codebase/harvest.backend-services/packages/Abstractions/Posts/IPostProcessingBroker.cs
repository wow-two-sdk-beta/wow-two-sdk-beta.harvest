using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Reads saved processing results without executing a processor or modifying captured facts.</summary>
public interface IPostProcessingBroker
{
    /// <summary>Reads the newest results for an unpurged post in the named container.</summary>
    /// <param name="containerId">The owning container.</param>
    /// <param name="postId">The captured post.</param>
    /// <param name="limit">The maximum results, from 1 to 100.</param>
    /// <param name="sourceId">An optional source restriction, applied before the result limit.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<Result<IReadOnlyList<PostProcessingModel>>> ListAsync(Guid containerId, Guid postId, int limit = 50,
        Guid? sourceId = null, CancellationToken cancellationToken = default);
}
