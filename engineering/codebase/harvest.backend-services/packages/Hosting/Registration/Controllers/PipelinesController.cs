using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Registration.Controllers;

/// <summary>Exposes pipelines over HTTP: <c>pipelines:manage</c> declares — the operator anywhere, a tenant in its own
/// private containers; <c>pipelines:read</c> reads the pool's and its own.</summary>
[ApiController]
[Route(RegistrationRouteConstants.Pipelines)]
public sealed class PipelinesController(IRegistrationBroker broker, CallerResolver callers) : ControllerBase
{
    /// <summary>Declares a pipeline.</summary>
    /// <param name="declaration">The pipeline to declare.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost]
    [Authorize(Policy = HarvestScopeConstants.PipelinesManage)]
    [ProducesResponseType<ApiResponse<PipelineModel>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Declare([FromBody] PipelineDeclaration declaration, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (!caller.IsOperator)
        {
            if (await broker.GetContainerAsync(declaration.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container }
                || !caller.CanRead(container))
                return this.ToProblem(RegistrationErrors.ContainerNotFound(declaration.ContainerId));
            if (!caller.CanWrite(container))
                return this.ToProblem(AppErrorFactory.Forbidden("A tenant declares pipelines in its own private containers; the pool is the operator's."));
        }

        return (await broker.DeclarePipelineAsync(declaration, ct)).Match<IActionResult>(
            pipeline => StatusCode(StatusCodes.Status201Created, ApiResponse<PipelineModel>.Ok(pipeline)),
            this.ToProblem);
    }

    /// <summary>Edits the saved scope and cadence in a container the caller owns.</summary>
    /// <param name="pipelineId">The pipeline's identifier.</param>
    /// <param name="update">The replacement name, scope and schedules.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPut("{pipelineId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesManage)]
    [ProducesResponseType<ApiResponse<PipelineModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid pipelineId, [FromBody] PipelineUpdate update, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetPipelineAsync(pipelineId, ct) is not Result<PipelineModel>.Success { Value: var pipeline }
            || await broker.GetContainerAsync(pipeline.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.PipelineNotFound(pipelineId));
        if (!caller.CanWrite(container))
            return this.ToProblem(AppErrorFactory.Forbidden("A tenant edits pipelines in its own private containers."));

        return (await broker.UpdatePipelineAsync(pipelineId, update, ct)).Match<IActionResult>(
            saved => Ok(ApiResponse<PipelineModel>.Ok(saved)), this.ToProblem);
    }

    /// <summary>Reads a pipeline in a container the caller may see.</summary>
    /// <param name="pipelineId">The pipeline's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{pipelineId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    [ProducesResponseType<ApiResponse<PipelineModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid pipelineId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetPipelineAsync(pipelineId, ct) is not Result<PipelineModel>.Success { Value: var pipeline })
            return this.ToProblem(RegistrationErrors.PipelineNotFound(pipelineId));

        return await broker.GetContainerAsync(pipeline.ContainerId, ct) is Result<ContainerModel>.Success { Value: var container }
               && caller.CanRead(container)
            ? Ok(ApiResponse<PipelineModel>.Ok(pipeline))
            : this.ToProblem(RegistrationErrors.PipelineNotFound(pipelineId));
    }

    /// <summary>Lists a container's pipelines.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<PipelineModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(containerId));

        return (await broker.ListPipelinesAsync(containerId, ct)).Match<IActionResult>(
            pipelines => Ok(ApiResponse<IReadOnlyList<PipelineModel>>.Ok(pipelines)),
            this.ToProblem);
    }
}
