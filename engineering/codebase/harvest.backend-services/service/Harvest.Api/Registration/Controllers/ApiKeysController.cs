using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Registration.Controllers;

/// <summary>Exposes API key metadata by key identifier — operator scope <c>tenants:manage</c>.</summary>
[ApiController]
[Route(RegistrationRouteConstants.ApiKeys)]
[Authorize(Policy = HarvestScopeConstants.TenantsManage)]
public sealed class ApiKeysController(IRegistrationBroker broker) : ControllerBase
{
    /// <summary>Reads an API key's metadata.</summary>
    /// <param name="keyId">The key's identifier.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("{keyId:guid}")]
    [ProducesResponseType<ApiResponse<ApiKeyModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid keyId, CancellationToken ct) =>
        (await broker.GetApiKeyAsync(keyId, ct)).Match<IActionResult>(
            key => Ok(ApiResponse<ApiKeyModel>.Ok(key)),
            this.ToProblem);
}
