using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace HarvestSources.Api.Processing;

/// <summary>Exposes the operator controls of the AI passes on the internal source host: re-runs over named posts, listed
/// sources or every source, their progress, and eval sets from stored posts. Operator only, as they spend on model calls
/// and rewrite stored answers across containers.</summary>
/// <remarks>Each route mirrors a <c>processing</c> host verb: <c>rerun</c>, <c>status</c>, <c>eval-set</c>.</remarks>
[ApiController]
[Authorize(Policy = AccessPolicies.ManageStorage)]
public sealed class ProcessingController(IPostReprocessingBroker reprocessing) : ControllerBase
{
    /// <summary>Queues a re-run, or with <c>dryRun</c> counts it and estimates its cost without queueing anything.</summary>
    /// <param name="request">The scope (<c>postIds</c>, <c>sourceIds</c> or <c>allSources</c>), the filters and the use cases.</param>
    /// <param name="ct">The token that cancels the request.</param>
    /// <returns><c>202</c> with the run and its status route; <c>200</c> with the counts of a dry run.</returns>
    [HttpPost(ProcessingRouteConstants.Reruns)]
    [ProducesResponseType<ApiResponse<PostRerunModel>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse<PostRerunModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rerun([FromBody] PostRerunRequest request, CancellationToken ct) =>
        (await reprocessing.RerunAsync(request, ct)).Match<IActionResult>(
            value => value.RunId is { } runId
                ? Accepted($"/{ProcessingRouteConstants.Reruns}/{runId:D}", ApiResponse<PostRerunModel>.Ok(value))
                : Ok(ApiResponse<PostRerunModel>.Ok(value)),
            this.ToProblem);

    /// <summary>Reads a re-run's record and what of it the queues still hold.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet(ProcessingRouteConstants.Reruns + "/{runId:guid}")]
    [ProducesResponseType<ApiResponse<PostRerunStatusModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(Guid runId, CancellationToken ct) =>
        (await reprocessing.GetRunAsync(runId, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<PostRerunStatusModel>.Ok(value)), this.ToProblem);

    /// <summary>Adds a seeded sample of stored posts to an eval set, creating it when missing; the AI API's eval routes then
    /// run models × prompt versions and report.</summary>
    /// <param name="request">The set, the use case, the sources and the sample.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost(ProcessingRouteConstants.EvalSets)]
    [ProducesResponseType<ApiResponse<PostEvalSetModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EvalSet([FromBody] PostEvalSetRequest request, CancellationToken ct) =>
        (await reprocessing.BuildEvalSetAsync(request, ct)).Match<IActionResult>(
            value => Ok(ApiResponse<PostEvalSetModel>.Ok(value)), this.ToProblem);
}
