using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace HarvestSources.Api.Marketplace;

/// <summary>Applies reviewed marketplace profiles under the operator's authority.</summary>
[ApiController]
[Route("api/marketplace")]
[Authorize(Policy = HarvestScopeConstants.TenantsManage)]
public sealed class MarketplaceController(MarketplaceProvisioner provisioner) : ControllerBase
{
    /// <summary>Lists the reviewed profiles without writing registry rows or starting network calls.</summary>
    [HttpGet("profiles")]
    public IActionResult Profiles() => Ok(ApiResponse<IReadOnlyList<MarketplaceProfile>>.Ok(MarketplaceProfiles.All));

    /// <summary>Creates the profile's missing rows; repeated calls keep existing rows, cadences and keys. The response
    /// carries a key secret only when this call issued it.</summary>
    [HttpPost("{profile}/setup")]
    public async Task<IActionResult> Setup(string profile, [FromBody] MarketplaceSetupRequest request, CancellationToken cancellationToken)
    {
        if (MarketplaceProfiles.Find(profile) is not { } found)
            return this.ToProblem(AppErrorFactory.NotFound($"No marketplace profile {profile}."));
        if (request.IssueKey && request.RotateKey)
            return this.ToProblem(AppErrorFactory.Validation("Ask for issueKey or rotateKey, not both."));
        var keys = request.RotateKey ? MarketplaceKeyAction.Rotate : request.IssueKey ? MarketplaceKeyAction.Issue : MarketplaceKeyAction.None;
        return (await provisioner.EnsureAsync(found, request.EnableSchedules, keys, cancellationToken)).Match<IActionResult>(
            value => Ok(ApiResponse<MarketplaceSetup>.Ok(value)), this.ToProblem);
    }

    /// <summary>Reads what setup owns for a profile, writing nothing.</summary>
    [HttpGet("{profile}/status")]
    public async Task<IActionResult> Status(string profile, CancellationToken cancellationToken) =>
        MarketplaceProfiles.Find(profile) is not { } found
            ? this.ToProblem(AppErrorFactory.NotFound($"No marketplace profile {profile}."))
            : (await provisioner.ReadAsync(found, cancellationToken)).Match<IActionResult>(
                value => Ok(ApiResponse<MarketplaceStatus>.Ok(value)), this.ToProblem);
}
