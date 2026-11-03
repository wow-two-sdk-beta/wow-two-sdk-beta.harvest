using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace Harvest.Api.Console;

/// <summary>Describes the console's authentication mode and the current caller's Harvest permissions without exposing credentials.</summary>
[ApiController]
[Route("api/console/access")]
public sealed class ConsoleController(IOptions<HarvestConsoleOptions> options, CallerResolver callers) : ControllerBase
{
    /// <summary>Reads the authentication mode and visible permissions; sign-in and session state belong to Identity's BFF endpoints.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var caller = await callers.ResolveAsync(User, cancellationToken);
        return Ok(ApiResponse<object>.Ok(new
        {
            identityEnabled = options.Value.IsIdentityEnabled,
            authenticated = User.Identity?.IsAuthenticated == true,
            hasAccess = caller is not null,
            tenantId = caller?.TenantId,
            scopes = caller?.Scopes ?? [],
        }));
    }
}
