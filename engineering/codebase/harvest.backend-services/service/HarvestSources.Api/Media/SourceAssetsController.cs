using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Beta.Be.Files.Client;
using WoW.Two.Sdk.Beta.Be.Files.Store;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace HarvestSources.Api.Media;

/// <summary>Streams private Files assets after checking access to their owning source.</summary>
[ApiController]
[Authorize(Policy = AccessPolicies.AnyCaller)]
public sealed class SourceAssetsController(IRegistrationBroker registry, CallerResolver callers, IServiceProvider services) : ControllerBase
{
    [HttpGet("/" + RegistrationRouteConstants.Sources + "/{sourceId:guid}/" + SourceRouteConstants.Assets + "/{assetId:guid}")]
    public async Task<IActionResult> Read(Guid sourceId, Guid assetId, CancellationToken ct)
    {
        if (await callers.ResolveAsync(User, ct) is not { } caller)
            return Unauthorized();
        if (services.GetService<SourceAssetStore>() is not { } assets
            || services.GetService<IFileStoreBroker>() is not { } files
            || await registry.GetSourceAsync(sourceId, ct) is not Result<SourceModel>.Success { Value: var source }
            || await registry.GetContainerAsync(source.ContainerId, ct) is not Result<ContainerModel>.Success { Value: var container }
            || !caller.CanRead(container)
            || await assets.FindAsync(sourceId, assetId, ct) is not { } asset)
            return NotFound();
        string scope = asset.Favicon ? HarvestScopeConstants.PipelinesRead : HarvestScopeConstants.PostsRead;
        if (!caller.Scopes.Contains(scope))
            return StatusCode(StatusCodes.Status403Forbidden);
        using var serviceCall = FilesClientCalls.AsService();
        if (await files.OpenReadAsync(new FileId(asset.FileId), cancellationToken: ct)
            is not Result<FileContent>.Success { Value: var content })
            return NotFound();
        if (SourceImageFormat.From(content.ContentType) is not { } format)
        {
            await content.Content.DisposeAsync();
            return NotFound();
        }
        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content.Content, format.ContentType);
    }
}
