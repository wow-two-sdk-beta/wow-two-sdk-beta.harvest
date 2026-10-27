using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Runs.Controllers;

/// <summary>Controls runs while keeping each tenant inside its containers.</summary>
[ApiController]
[Route("api/runs")]
public sealed class RunsController(IRunBroker runs, IRegistrationBroker registry, CallerResolver callers) : ControllerBase
{
    /// <summary>Queues work in a writable pipeline.</summary>
    [HttpPost]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRun)]
    public async Task<IActionResult> Start([FromBody] StartRunRequest request, CancellationToken ct)
    {
        if (!await AllowedAsync(request.PipelineId, true, ct))
            return this.ToProblem(RegistrationErrors.PipelineNotFound(request.PipelineId));
        return (await runs.StartAsync(request.PipelineId, request.Kind, ct)).Match<IActionResult>(
            value => Accepted(ApiResponse<RunModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Lists recent runs of a visible pipeline.</summary>
    [HttpGet]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    public async Task<IActionResult> List([FromQuery] Guid pipelineId, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        if (!await AllowedAsync(pipelineId, false, ct))
            return this.ToProblem(RegistrationErrors.PipelineNotFound(pipelineId));
        return (await runs.ListAsync(pipelineId, limit, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<RunModel>>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads a run in a visible container.</summary>
    [HttpGet("{runId:guid}")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    public async Task<IActionResult> Get(Guid runId, CancellationToken ct)
    {
        if (await runs.GetAsync(runId, ct) is not Result<RunModel>.Success { Value: var run }
            || !await AllowedAsync(run.PipelineId, false, ct))
            return this.ToProblem(AppErrorFactory.NotFound("The run was not found."));
        return Ok(ApiResponse<RunModel>.Ok(run));
    }

    /// <summary>Cancels outstanding work without removing its history.</summary>
    [HttpPost("{runId:guid}/cancel")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRun)]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken ct)
    {
        if (await runs.GetAsync(runId, ct) is not Result<RunModel>.Success { Value: var run }
            || !await AllowedAsync(run.PipelineId, true, ct))
            return this.ToProblem(AppErrorFactory.NotFound("The run was not found."));
        return (await runs.CancelAsync(runId, ct)).Match<IActionResult>(value => Ok(ApiResponse<RunModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Pauses or resumes a writable pipeline.</summary>
    [HttpPost("/api/pipelines/{pipelineId:guid}/pause")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesManage)]
    public async Task<IActionResult> Pause(Guid pipelineId, [FromBody] PausePipelineRequest request, CancellationToken ct)
    {
        if (!await AllowedAsync(pipelineId, true, ct))
            return this.ToProblem(RegistrationErrors.PipelineNotFound(pipelineId));
        return (await runs.SetPausedAsync(pipelineId, request.Paused, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<PipelineModel>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads host lanes without exposing another container's queue.</summary>
    [HttpGet("/api/hosts")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    public async Task<IActionResult> Hosts([FromQuery] Guid containerId, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || await registry.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(containerId));
        return (await runs.HostsAsync(containerId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<IReadOnlyList<HostHealthModel>>.Ok(value)), this.ToProblem);
    }

    private async Task<bool> AllowedAsync(Guid pipelineId, bool write, CancellationToken ct)
    {
        Caller? caller = await callers.ResolveAsync(User, ct);
        if (caller is null || await registry.GetPipelineAsync(pipelineId, ct) is not Result<PipelineModel>.Success { Value: var pipeline }
            || await registry.GetContainerAsync(pipeline.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container })
            return false;
        return write ? caller.CanWrite(container) : caller.CanRead(container);
    }
}
