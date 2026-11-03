using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;

namespace Harvest.Api.Console;

/// <summary>Authorizes API keys or explicitly bound Identity sessions without promoting an organization member to operator.</summary>
public sealed class HarvestAccessAuthorizationHandler(CallerResolver callers, IHttpContextAccessor contexts)
    : AuthorizationHandler<HarvestAccessRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HarvestAccessRequirement requirement)
    {
        var cancellationToken = contexts.HttpContext?.RequestAborted ?? CancellationToken.None;
        var caller = await callers.ResolveAsync(context.User, cancellationToken);
        if (caller is null || (requirement.IsTenantRead && !caller.IsOperator && !caller.IsIdentitySession))
            return;
        if (requirement.Scope is null || caller.Scopes.Contains(requirement.Scope, StringComparer.Ordinal))
            context.Succeed(requirement);
    }
}
