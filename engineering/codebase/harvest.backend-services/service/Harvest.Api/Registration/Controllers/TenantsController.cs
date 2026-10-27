using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Registration.Controllers;

/// <summary>Exposes tenants and their API keys over HTTP — operator scope <c>tenants:manage</c>.</summary>
[ApiController]
[Route(RegistrationRouteConstants.Tenants)]
[Authorize(Policy = HarvestScopeConstants.TenantsManage)]
public sealed class TenantsController(IRegistrationBroker broker) : ControllerBase
{
    /// <summary>Registers a tenant.</summary>
    /// <param name="registration">The tenant to register.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost]
    [ProducesResponseType<ApiResponse<TenantModel>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register([FromBody] TenantRegistration registration, CancellationToken ct) =>
        (await broker.RegisterTenantAsync(registration, ct)).Match<IActionResult>(
            tenant => StatusCode(StatusCodes.Status201Created, ApiResponse<TenantModel>.Ok(tenant)),
            this.ToProblem);

    /// <summary>Lists every tenant.</summary>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<TenantModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        (await broker.ListTenantsAsync(ct)).Match<IActionResult>(
            tenants => Ok(ApiResponse<IReadOnlyList<TenantModel>>.Ok(tenants)),
            this.ToProblem);

    /// <summary>Reads a tenant.</summary>
    /// <param name="tenantId">The tenant's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{tenantId:guid}")]
    [ProducesResponseType<ApiResponse<TenantModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct) =>
        (await broker.GetTenantAsync(tenantId, ct)).Match<IActionResult>(
            tenant => Ok(ApiResponse<TenantModel>.Ok(tenant)),
            this.ToProblem);

    /// <summary>Issues an API key to a tenant; the secret is in this answer only.</summary>
    /// <param name="tenantId">The tenant the key acts for.</param>
    /// <param name="issue">The key's name, scopes and expiry.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPost("{tenantId:guid}/" + RegistrationRouteConstants.TenantApiKeys)]
    [ProducesResponseType<ApiResponse<IssuedApiKeyModel>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> IssueKey(Guid tenantId, [FromBody] ApiKeyIssue issue, CancellationToken ct) =>
        (await broker.IssueApiKeyAsync(tenantId, issue, ct)).Match<IActionResult>(
            key => StatusCode(StatusCodes.Status201Created, ApiResponse<IssuedApiKeyModel>.Ok(key)),
            this.ToProblem);

    /// <summary>Lists a tenant's API keys.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{tenantId:guid}/" + RegistrationRouteConstants.TenantApiKeys)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<ApiKeyModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListKeys(Guid tenantId, CancellationToken ct) =>
        (await broker.ListApiKeysAsync(tenantId, ct)).Match<IActionResult>(
            keys => Ok(ApiResponse<IReadOnlyList<ApiKeyModel>>.Ok(keys)),
            this.ToProblem);

    /// <summary>Revokes a tenant's API key.</summary>
    /// <param name="tenantId">The tenant the key acts for.</param>
    /// <param name="keyId">The key's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpDelete("{tenantId:guid}/" + RegistrationRouteConstants.TenantApiKeys + "/{keyId:guid}")]
    [ProducesResponseType<ApiResponse<ApiKeyModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeKey(Guid tenantId, Guid keyId, CancellationToken ct) =>
        (await broker.RevokeApiKeyAsync(tenantId, keyId, ct)).Match<IActionResult>(
            key => Ok(ApiResponse<ApiKeyModel>.Ok(key)),
            this.ToProblem);
}
