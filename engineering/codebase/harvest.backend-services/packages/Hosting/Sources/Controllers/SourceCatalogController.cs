using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace Harvest.Api.Sources.Controllers;

/// <summary>Exposes the instance's source catalog to platform operators: which registered sources this instance turns on. The read
/// lists the whole registry, not one tenant's sources, so it needs the operator too.</summary>
[ApiController]
public sealed class SourceCatalogController(SourceCatalogBroker catalog) : ControllerBase
{
    /// <summary>Lists every registered entry: key, catalog, name, hosts, channel count, state, who changed it and when, and the
    /// sources it set up.</summary>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpGet("/" + SourceRouteConstants.Catalog)]
    [Authorize(Policy = AccessPolicies.ManageStorage)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<CatalogSourceModel>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CatalogSourceModel>>.Ok(await catalog.ListAsync(ct)));

    /// <summary>Turns an entry on or off. A <c>new</c> entry turned on is set up first, with schedules and model processing off; a
    /// set-up that fails answers <c>409</c> and the record stays. An unknown key answers <c>404</c>.</summary>
    /// <param name="key">The entry's key, such as <c>ocharo.afisha</c>.</param>
    /// <param name="request">Whether the entry is turned on.</param>
    /// <param name="ct">The token that cancels the request.</param>
    [HttpPut("/" + SourceRouteConstants.Catalog + "/{key}")]
    [Authorize(Policy = AccessPolicies.ManageSources)]
    [ProducesResponseType<ApiResponse<CatalogSourceModel>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Switch(string key, [FromBody] CatalogSwitchRequest request, CancellationToken ct) =>
        (await catalog.SwitchAsync(key, request.TurnedOn, ChangedBy(), ct)).Match<IActionResult>(
            value => Ok(ApiResponse<CatalogSourceModel>.Ok(value)), this.ToProblem);

    /// <summary>Names the caller for the record: the Identity subject of a session or service client, else <c>operator-key</c>.</summary>
    private string ChangedBy()
    {
        var key = User.Identities.Where(identity => identity.IsAuthenticated)
            .Select(identity => identity.FindFirst(ApiKeyAuthenticationDefaults.KeyIdClaim)?.Value).FirstOrDefault(value => value is not null);
        if (key is not null)
            return SourceCatalogConstants.ChangedByOperatorKey;
        string? subject = User.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(subject) ? SourceCatalogConstants.ChangedByOperatorKey : subject;
    }
}
