using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Web.Contracts;

namespace Harvest.Api.Console;

/// <summary>Exposes the console sign-in mode and the session's SCFD permissions.</summary>
[ApiController]
[Route("api/console/access")]
public sealed class ConsoleAccessController(IOptions<HarvestConsoleOptions> options, CallerResolver callers) : ControllerBase
{
    /// <summary>Reads the sign-in mode and permissions available through the browser session.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var caller = await callers.ResolveAsync(User, cancellationToken);
        var session = caller is { IsIdentitySession: true } ? caller : null;
        return Ok(ApiResponse<object>.Ok(new
        {
            identityEnabled = options.Value.IsIdentityEnabled,
            hasAccess = session is not null,
            isOperator = session?.IsOperator ?? false,
            tenantId = session?.TenantId,
            scopes = session?.Scopes ?? [],
        }));
    }
}
