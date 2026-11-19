using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Reads the console's Sources area: each source's overview, the posts one source shows and a post's
/// revisions.</summary>
/// <remarks>Read-only over the registry, run, post and delivery stores; <c>SourceOverviewBrokerConformance</c> runs
/// against every form. Failures are <c>Validation</c> for a malformed request and <c>NotFound</c> for an unknown
/// reference; the HTTP form adds <c>Unauthorized</c>, <c>Forbidden</c> and <c>ExternalUnavailable</c>.</remarks>
public interface ISourceOverviewBroker
{
    /// <summary>Reads one overview per registered source of a container; no source is invented.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The overviews, by name; <c>NotFound</c> for an unknown container.</returns>
    Task<Result<IReadOnlyList<SourceOverviewModel>>> ListOverviewAsync(Guid containerId, CancellationToken cancellationToken = default);

    /// <summary>Lists the surviving posts a source shows, newest first.</summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="limit">The most posts to return, 1–500.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The posts; <c>NotFound</c> for an unknown source, <c>Validation</c> for a limit outside 1–500.</returns>
    Task<Result<IReadOnlyList<SourcePostModel>>> ListSourcePostsAsync(Guid sourceId, int limit = SourceConstants.DefaultPostsLimit, CancellationToken cancellationToken = default);

    /// <summary>Searches a source's posts with counts across the full result set and an optional continuation.</summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="query">The search, facets, page size and cursor.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A page and its facet counts, or a validation or source lookup error.</returns>
    Task<Result<SourcePostPage>> QuerySourcePostsAsync(Guid sourceId, SourcePostQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lists one page of the source's posts the AI passes processed — every surviving post with a
    /// <c>post-tags</c> answer — with its tags, verdict and extraction summary, newest first seen first.</summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="query">The filters, page size and cursor (<see cref="SourceProcessedQueryRules"/>).</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The page and the total over its filters; <c>NotFound</c> for an unknown source, <c>Validation</c> for
    /// a filter, limit or cursor the rules refuse.</returns>
    Task<Result<SourceProcessedPage>> QuerySourceProcessedAsync(Guid sourceId, SourceProcessedQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lists a post's revisions, newest first; a post from before revisions were kept has none.</summary>
    /// <param name="containerId">The container that owns the post.</param>
    /// <param name="postId">The post.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The revisions; <c>NotFound</c> when the container holds no such post.</returns>
    Task<Result<IReadOnlyList<PostRevisionModel>>> ListPostRevisionsAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default);
}
