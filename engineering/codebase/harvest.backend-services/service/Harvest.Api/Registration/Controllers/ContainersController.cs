using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Registration.Controllers;

/// <summary>Exposes containers over HTTP: the operator registers them (<c>containers:manage</c>); any key reads the pool
/// and its own tenant's.</summary>
[ApiController]
[Route(RegistrationRouteConstants.Containers)]
public sealed class ContainersController(IRegistrationBroker broker, CallerResolver callers) : ControllerBase
{
    /// <summary>Registers the pool or a tenant's private container.</summary>
    /// <param name="registration">The container to register.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost]
    [Authorize(Policy = HarvestScopeConstants.ContainersManage)]
    [ProducesResponseType<ApiResponse<ContainerModel>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] ContainerRegistration registration, CancellationToken ct) =>
        (await broker.RegisterContainerAsync(registration, ct)).Match<IActionResult>(
            container => StatusCode(StatusCodes.Status201Created, ApiResponse<ContainerModel>.Ok(container)),
            this.ToProblem);

    /// <summary>Reads a container the caller may see.</summary>
    /// <param name="containerId">The container's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{containerId:guid}")]
    [Authorize(Policy = AccessPolicies.AnyKey)]
    [ProducesResponseType<ApiResponse<ContainerModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid containerId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());

        return await broker.GetContainerAsync(containerId, ct) switch
        {
            Result<ContainerModel>.Success { Value: var container } when caller.CanRead(container) =>
                Ok(ApiResponse<ContainerModel>.Ok(container)),
            Result<ContainerModel>.Failure { Error: var error } => this.ToProblem(error),
            _ => this.ToProblem(RegistrationErrors.ContainerNotFound(containerId)),
        };
    }

    /// <summary>Lists containers: for the operator, every container or one tenant's; for a tenant, the pool and its own.</summary>
    /// <param name="tenantId">The tenant whose private containers the operator lists.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet]
    [Authorize(Policy = AccessPolicies.AnyKey)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ContainerModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery(Name = RegistrationRouteConstants.TenantIdParameter)] Guid? tenantId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return this.ToProblem(AppErrorFactory.Unauthorized());

        var listed = await broker.ListContainersAsync(caller.IsOperator ? tenantId : null, ct);
        return listed.Match<IActionResult>(
            containers => Ok(ApiResponse<IReadOnlyList<ContainerModel>>.Ok(
                [.. containers.Where(container => caller.IsOperator || (caller.CanRead(container) && (tenantId is null || container.TenantId == tenantId)))])),
            this.ToProblem);
    }
}
