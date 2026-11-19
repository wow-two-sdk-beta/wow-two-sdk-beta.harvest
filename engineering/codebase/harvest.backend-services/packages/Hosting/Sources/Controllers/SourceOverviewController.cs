using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Sources.Controllers;

/// <summary>Exposes the console's Sources reads inside the containers a caller may see: the overview and groups take
/// <c>pipelines:read</c>, a source's posts and a post's revisions take <c>posts:read</c>.</summary>
[ApiController]
public sealed class SourceOverviewController(ISourceOverviewBroker overview, IRegistrationBroker registry, CallerResolver callers) : ControllerBase
{
    /// <summary>Reads one overview per registered source of a visible container.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("/" + SourceRouteConstants.Overview)]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SourceOverviewModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Overview([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await registry.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(containerId));

        return (await overview.ListOverviewAsync(containerId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<SourceOverviewModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Lists the posts a visible source shows, newest first.</summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="limit">The most posts to return, 1–500.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("/" + RegistrationRouteConstants.Sources + "/{sourceId:guid}/" + SourceRouteConstants.Posts)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SourcePostModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Posts(Guid sourceId, [FromQuery(Name = SourceRouteConstants.LimitParameter)] int limit = SourceConstants.DefaultPostsLimit,
        CancellationToken ct = default)
    {
        if (await SourceAccessErrorAsync(sourceId, ct) is { } error)
            return this.ToProblem(error);
        return (await overview.ListSourcePostsAsync(sourceId, limit, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<SourcePostModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Queries the visible source's full Posts collection and returns one page with facet counts.</summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="query">The search, filters, and page request.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost("/" + RegistrationRouteConstants.Sources + "/{sourceId:guid}/" + SourceRouteConstants.PostQuery)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [RequestSizeLimit(16_384)]
    [ProducesResponseType<ApiResponse<SourcePostPage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> QueryPosts(Guid sourceId, [FromBody] SourcePostQuery query, CancellationToken ct)
    {
        if (await SourceAccessErrorAsync(sourceId, ct) is { } error)
            return this.ToProblem(error);
        return (await overview.QuerySourcePostsAsync(sourceId, query, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<SourcePostPage>.Ok(value)), this.ToProblem);
    }

    /// <summary>Lists one page of the visible source's processed posts: tags, verdict and extraction summary, with
    /// each answer's provider, model, prompt version, cost and latency.</summary>
    /// <param name="sourceId">The source.</param>
    /// <param name="entityType">The tags' entity type: <c>event</c>, <c>listing</c>, <c>none</c> or <c>unsure</c>.</param>
    /// <param name="verdict">The verdict: <c>relevant</c>, <c>irrelevant</c> or <c>uncertain</c>.</param>
    /// <param name="extracted">Whether a done extraction answer must exist.</param>
    /// <param name="limit">The page size, 1–90.</param>
    /// <param name="cursor">The cursor a previous page returned.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("/" + RegistrationRouteConstants.Sources + "/{sourceId:guid}/" + SourceRouteConstants.Processed)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<SourceProcessedPage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Processed(Guid sourceId,
        [FromQuery(Name = SourceRouteConstants.EntityTypeParameter)] string? entityType,
        [FromQuery(Name = SourceRouteConstants.VerdictParameter)] string? verdict,
        [FromQuery(Name = SourceRouteConstants.ExtractedParameter)] bool? extracted,
        [FromQuery(Name = SourceRouteConstants.LimitParameter)] int limit = SourceProcessedQuery.DefaultLimit,
        [FromQuery(Name = SourceRouteConstants.CursorParameter)] string? cursor = null, CancellationToken ct = default)
    {
        if (await SourceAccessErrorAsync(sourceId, ct) is { } error)
            return this.ToProblem(error);
        ClassificationVerdict? parsed = null;
        if (!string.IsNullOrEmpty(verdict))
        {
            if (!Enum.TryParse<ClassificationVerdict>(verdict, ignoreCase: true, out var value) || !Enum.IsDefined(value)
                || verdict.Any(char.IsDigit))
                return this.ToProblem(RegistrationErrors.Invalid("The verdict must be relevant, irrelevant or uncertain."));
            parsed = value;
        }
        var query = new SourceProcessedQuery
        {
            EntityType = string.IsNullOrEmpty(entityType) ? null : entityType, Verdict = parsed, Extracted = extracted,
            Limit = limit, Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
        };
        return (await overview.QuerySourceProcessedAsync(sourceId, query, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<SourceProcessedPage>.Ok(value)), this.ToProblem);
    }

    private async Task<AppError?> SourceAccessErrorAsync(Guid sourceId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return AppErrorFactory.Unauthorized();
        if (await registry.GetSourceAsync(sourceId, ct) is not Result<SourceModel>.Success { Value: var source }
            || await registry.GetContainerAsync(source.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return RegistrationErrors.SourceNotFound(sourceId);
        return null;
    }

    /// <summary>Lists a visible post's revisions, newest first; the container may be named, else the caller's visible
    /// containers are searched.</summary>
    /// <param name="postId">The post.</param>
    /// <param name="containerId">The post's container, when the caller knows it.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("/" + PostRouteConstants.Posts + "/{postId:guid}/" + SourceRouteConstants.Revisions)]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PostRevisionModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revisions(Guid postId, [FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid? containerId,
        CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        IReadOnlyList<ContainerModel> candidates;
        if (containerId is { } named)
            candidates = await registry.GetContainerAsync(named, ct) is Result<ContainerModel>.Success { Value: var container } ? [container] : [];
        else if (await registry.ListContainersAsync(null, ct) is Result<IReadOnlyList<ContainerModel>>.Success { Value: var containers })
            candidates = containers;
        else
            candidates = [];

        foreach (var candidate in candidates.Where(caller.CanRead))
            if (await overview.ListPostRevisionsAsync(candidate.Id, postId, ct) is Result<IReadOnlyList<PostRevisionModel>>.Success { Value: var revisions })
                return Ok(ApiResponse<IReadOnlyList<PostRevisionModel>>.Ok(revisions));
        return this.ToProblem(AppErrorFactory.NotFound("The post was not found."));
    }
}
