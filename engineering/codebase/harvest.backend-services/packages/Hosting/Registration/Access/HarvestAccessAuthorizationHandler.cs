using Microsoft.AspNetCore.Authorization;

namespace Harvest.Api.Registration.Access;

/// <summary>Authorizes a live Harvest caller and its granted scopes.</summary>
public sealed class HarvestAccessAuthorizationHandler(CallerResolver callers, IHttpContextAccessor contexts)
    : AuthorizationHandler<HarvestAccessRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HarvestAccessRequirement requirement)
    {
        var cancellationToken = contexts.HttpContext?.RequestAborted ?? CancellationToken.None;
        var caller = await callers.ResolveAsync(context.User, cancellationToken);
        if (caller is null)
            return;
        if (requirement.Scope is null || caller.Scopes.Contains(requirement.Scope, StringComparer.Ordinal))
            context.Succeed(requirement);
    }
}
