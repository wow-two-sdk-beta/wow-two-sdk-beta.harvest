using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Posts.Controllers;

/// <summary>Exposes versioned processor results to authorized post readers.</summary>
[ApiController]
[Route("api/posts/{postId:guid}/processing")]
public sealed class PostProcessingController(IPostProcessingBroker results, IRegistrationBroker registry, CallerResolver callers) : ControllerBase
{
    /// <summary>Reads results for a visible post without starting a model call.</summary>
    [HttpGet]
    [Authorize(Policy = HarvestScopeConstants.PostsRead)]
    public async Task<IActionResult> List(Guid postId, [FromQuery] Guid containerId, [FromQuery] int limit = 50,
        [FromQuery] Guid? sourceId = null, CancellationToken ct = default)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || await registry.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(AppErrorFactory.NotFound("The post was not found."));
        var read = await results.ListAsync(containerId, postId, limit, sourceId, ct);
        if (read is Result<IReadOnlyList<PostProcessingModel>>.Failure failure)
            return this.ToProblem(failure.Error);
        var value = ((Result<IReadOnlyList<PostProcessingModel>>.Success)read).Value;
        if (!caller.IsOperator && container.Kind == ContainerKind.Pool)
        {
            var visible = new HashSet<Guid>();
            foreach (Guid resultSourceId in value.Select(item => item.SourceId).Distinct())
                if (await registry.GetSourceAsync(resultSourceId, ct) is Result<SourceModel>.Success { Value: var source }
                    && source.ContainerId == containerId && source.PoolShareable)
                    visible.Add(resultSourceId);
            value = value.Where(item => visible.Contains(item.SourceId)).ToArray();
        }
        return Ok(ApiResponse<IReadOnlyList<PostProcessingModel>>.Ok(value));
    }
}
