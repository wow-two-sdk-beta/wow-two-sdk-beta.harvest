using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Posts.Controllers;

/// <summary>Exposes post queries and operator-only ingestion and merge controls.</summary>
[ApiController]
[Route("api/posts")]
public sealed class PostsController(IPostBroker posts, IRegistrationBroker registry, CallerResolver callers) : ControllerBase
{
    /// <summary>Lists canonical posts inside a visible container.</summary>
    [HttpGet]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    public async Task<IActionResult> List([FromQuery] Guid containerId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        if (!await VisibleAsync(containerId, ct))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(containerId));
        return (await posts.ListPostsAsync(containerId, limit, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<PostModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads a canonical post and its link provenance.</summary>
    [HttpGet("{postId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    public async Task<IActionResult> Get(Guid postId, [FromQuery] Guid containerId, CancellationToken ct)
    {
        if (!await VisibleAsync(containerId, ct))
            return this.ToProblem(AppErrorFactory.NotFound("The post was not found."));
        return (await posts.GetPostAsync(containerId, postId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<PostModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Mirrors in-process capture ingestion for an operator client.</summary>
    [HttpPost("captures")]
    [Authorize(Policy = HarvestScopeConstants.ContainersManage)]
    public async Task<IActionResult> Capture([FromBody] SaveCaptureRequest request, CancellationToken ct)
    {
        return (await posts.SaveCaptureAsync(request.ContainerId, request.SourceId, request.RunId,
            request.Capture, request.Envelope, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<CaptureSaveResult>.Ok(value)), this.ToProblem);
    }

    /// <summary>Mirrors a known link observation for an operator client.</summary>
    [HttpPost("observations")]
    [Authorize(Policy = HarvestScopeConstants.ContainersManage)]
    public async Task<IActionResult> Observe([FromBody] LinkObservationRequest request, CancellationToken ct)
    {
        return (await posts.ObserveLinkAsync(request.ContainerId, request.SourceId, request.Observation,
            request.RunId, ct)).Match<IActionResult>(value => Ok(ApiResponse<PostModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Merges two posts and keeps a durable undo record.</summary>
    [HttpPost("/api/post-merges")]
    [Authorize(Policy = HarvestScopeConstants.ContainersManage)]
    public async Task<IActionResult> Merge([FromBody] MergePostsRequest request, CancellationToken ct)
    {
        return (await posts.MergeAsync(request.ContainerId, request.SurvivorId, request.LoserId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<PostMergeModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Restores merged links and keeps the separated pair apart.</summary>
    [HttpDelete("/api/post-merges/{mergeId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.ContainersManage)]
    public async Task<IActionResult> Unmerge(Guid mergeId, [FromQuery] Guid containerId, CancellationToken ct)
    {
        return (await posts.UnmergeAsync(containerId, mergeId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<PostMergeModel>.Ok(value)), this.ToProblem);
    }

    private async Task<bool> VisibleAsync(Guid containerId, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        return caller is not null && await registry.GetContainerAsync(containerId, ct) is Result<ContainerModel>.Success { Value: var container }
            && caller.CanRead(container);
    }
}
