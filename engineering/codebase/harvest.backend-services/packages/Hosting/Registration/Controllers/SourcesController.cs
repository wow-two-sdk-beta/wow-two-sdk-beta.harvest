using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Registration.Controllers;

/// <summary>Exposes sources over HTTP: the operator registers them (<c>sources:manage</c>), names and groups them; any key
/// reads the sources of the containers it may see, and <c>pipelines:read</c> reads their groups.</summary>
[ApiController]
[Route(RegistrationRouteConstants.Sources)]
public sealed class SourcesController(IRegistrationBroker broker, CallerResolver callers, Harvest.Api.Registration.SourceCapabilitiesResolver capabilities) : ControllerBase
{
    /// <summary>Registers a source in a container.</summary>
    /// <param name="registration">The source to register.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost]
    [Authorize(Policy = HarvestScopeConstants.SourcesManage)]
    [ProducesResponseType<ApiResponse<SourceModel>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] SourceRegistration registration, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetContainerAsync(registration.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(registration.ContainerId));
        if (!caller.CanWrite(container))
            return this.ToProblem(AppErrorFactory.Forbidden("A tenant registers sources only in its own private containers."));
        return (await broker.RegisterSourceAsync(registration, ct)).Match<IActionResult>(
            source => StatusCode(StatusCodes.Status201Created, ApiResponse<SourceModel>.Ok(source)), this.ToProblem);
    }

    /// <summary>Sets a source's display name and group (operator only).</summary>
    /// <param name="sourceId">The source's identifier.</param>
    /// <param name="update">The name and group to set; <c>clearGroup</c> ungroups.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPut("{sourceId:guid}")]
    [Authorize(Policy = AccessPolicies.ManageSources)]
    [ProducesResponseType<ApiResponse<SourceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid sourceId, [FromBody] SourceUpdate update, CancellationToken ct) =>
        (await broker.UpdateSourceAsync(sourceId, update, ct)).Match<IActionResult>(
            source => Ok(ApiResponse<SourceModel>.Ok(source)), this.ToProblem);

    /// <summary>Declares a group of sources, idempotent by name (operator only).</summary>
    /// <param name="declaration">The group to declare.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost("groups")]
    [Authorize(Policy = AccessPolicies.ManageSources)]
    [ProducesResponseType<ApiResponse<SourceGroupModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclareGroup([FromBody] SourceGroupDeclaration declaration, CancellationToken ct) =>
        (await broker.DeclareSourceGroupAsync(declaration, ct)).Match<IActionResult>(
            group => Ok(ApiResponse<SourceGroupModel>.Ok(group)), this.ToProblem);

    /// <summary>Lists the source groups of a container the caller may see.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("groups")]
    [Authorize(Policy = HarvestScopeConstants.PipelinesRead)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SourceGroupModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListGroups([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(containerId));

        return (await broker.ListSourceGroupsAsync(containerId, ct)).Match<IActionResult>(
            groups => Ok(ApiResponse<IReadOnlyList<SourceGroupModel>>.Ok(groups)), this.ToProblem);
    }

    /// <summary>Reads a source in a container the caller may see.</summary>
    /// <param name="sourceId">The source's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{sourceId:guid}")]
    [Authorize(Policy = AccessPolicies.AnyCaller)]
    [ProducesResponseType<ApiResponse<SourceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid sourceId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetSourceAsync(sourceId, ct) is not Result<SourceModel>.Success { Value: var source })
            return this.ToProblem(RegistrationErrors.SourceNotFound(sourceId));

        return await broker.GetContainerAsync(source.ContainerId, ct) is Result<ContainerModel>.Success { Value: var container }
               && caller.CanRead(container)
            ? Ok(ApiResponse<SourceModel>.Ok(source))
            : this.ToProblem(RegistrationErrors.SourceNotFound(sourceId));
    }

    /// <summary>Reads installed adapter metadata for a source the caller may see; never probes the source.</summary>
    /// <param name="sourceId">The visible source.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{sourceId:guid}/capabilities")]
    [Authorize(Policy = AccessPolicies.AnyCaller)]
    [ProducesResponseType<ApiResponse<SourceCapabilitiesModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Capabilities(Guid sourceId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetSourceAsync(sourceId, ct) is not Result<SourceModel>.Success { Value: var source })
            return this.ToProblem(RegistrationErrors.SourceNotFound(sourceId));
        if (await broker.GetContainerAsync(source.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.SourceNotFound(sourceId));
        return Ok(ApiResponse<SourceCapabilitiesModel>.Ok(capabilities.Describe(source)));
    }

    /// <summary>Lists a container's sources.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet]
    [Authorize(Policy = AccessPolicies.AnyCaller)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<SourceModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery(Name = RegistrationRouteConstants.ContainerIdParameter)] Guid containerId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());
        if (await broker.GetContainerAsync(containerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container))
            return this.ToProblem(RegistrationErrors.ContainerNotFound(containerId));

        return (await broker.ListSourcesAsync(containerId, ct)).Match<IActionResult>(
            sources => Ok(ApiResponse<IReadOnlyList<SourceModel>>.Ok(sources)),
            this.ToProblem);
    }
}
