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

/// <summary>Exposes sources over HTTP: the operator registers them (<c>sources:manage</c>); any key reads the sources of
/// the containers it may see.</summary>
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
    public async Task<IActionResult> Register([FromBody] SourceRegistration registration, CancellationToken ct) =>
        (await broker.RegisterSourceAsync(registration, ct)).Match<IActionResult>(
            source => StatusCode(StatusCodes.Status201Created, ApiResponse<SourceModel>.Ok(source)),
            this.ToProblem);

    /// <summary>Reads a source in a container the caller may see.</summary>
    /// <param name="sourceId">The source's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{sourceId:guid}")]
    [Authorize(Policy = AccessPolicies.AnyKey)]
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
    [Authorize(Policy = AccessPolicies.AnyKey)]
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
    [Authorize(Policy = AccessPolicies.AnyKey)]
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
