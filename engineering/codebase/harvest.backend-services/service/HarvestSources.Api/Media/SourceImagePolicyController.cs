using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Files.Store;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace HarvestSources.Api.Media;

/// <summary>Exposes the instance image policy to SCFD operators.</summary>
[ApiController]
[Route("api/settings/source-images")]
[Authorize(Policy = HarvestScopeConstants.TenantsManage)]
public sealed class SourceImagePolicyController(CallerResolver callers, IServiceProvider services, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { IsOperator: true })
            return Forbid();

        return Answer(await services.GetRequiredService<SourceImagePolicyStore>().ReadAsync(ct));
    }

    [HttpPut]
    public async Task<IActionResult> Save([FromBody] SourceImagePolicyUpdate update, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { IsOperator: true })
            return Forbid();
        if (update.PostPresets is not { Count: <= 5 } presets || presets.Any(name => !ImageVariantPresets.TryGetWidth(name, out _)))
            return BadRequest(new ProblemDetails { Title = "Choose up to five image presets from s1 through s5." });

        return Answer(await services.GetRequiredService<SourceImagePolicyStore>()
            .SaveAsync(presets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), ct));
    }

    private IActionResult Answer(string[] presets) =>
        Ok(ApiResponse<SourceImagePolicyModel>.Ok(new(presets, !string.IsNullOrWhiteSpace(configuration["HarvestAssets:Files:BaseAddress"]))));
}

/// <summary>Describes the post image presets and the Files connection's configuration state.</summary>
public sealed record SourceImagePolicyModel(IReadOnlyList<string> PostPresets, bool FilesConfigured);

/// <summary>Requests the preset selection for future media copies. An empty selection keeps the originals.</summary>
public sealed record SourceImagePolicyUpdate(IReadOnlyList<string>? PostPresets);
