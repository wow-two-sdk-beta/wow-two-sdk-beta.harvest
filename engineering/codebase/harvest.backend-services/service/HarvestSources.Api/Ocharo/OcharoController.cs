using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;
using WoW.Two.Sdk.Beta.Be.Web.ExceptionHandling.Extensions;

namespace HarvestSources.Api.Ocharo;

/// <summary>Applies the reviewed private source preset under the operator's authority.</summary>
[ApiController]
[Route("api/ocharo/sources")]
[Authorize(Policy = HarvestScopeConstants.TenantsManage)]
public sealed class OcharoController(OcharoSourceProvisioner provisioner) : ControllerBase
{
    /// <summary>Lists the reviewed presets without writing registry rows or starting network calls.</summary>
    [HttpGet("presets")]
    public IActionResult Presets() => Ok(ApiResponse<IReadOnlyList<OcharoSourcePreset>>.Ok([.. OcharoSourcePreset.All, OcharoSourcePreset.Instagram]));

    /// <summary>Lists publisher groups, channel roles and collection strategies without starting any runs.</summary>
    [HttpGet("registry")]
    public IActionResult Registry() => Ok(ApiResponse<OcharoSourceRegistry>.Ok(OcharoSourceRegistry.Current));

    /// <summary>Creates missing private registry rows; repeated calls preserve existing operator settings.</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup([FromBody] OcharoSetupRequest request, CancellationToken cancellationToken) =>
        (await provisioner.EnsureAsync(request.EnableSchedules, cancellationToken, request.IncludeInstagram, request.GroupIds)).Match<IActionResult>(
            value => Ok(ApiResponse<OcharoSetup>.Ok(value)), this.ToProblem);
}
