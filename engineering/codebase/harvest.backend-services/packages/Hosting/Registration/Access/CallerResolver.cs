using System.Security.Claims;
using Harvest.Api.Console;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace Harvest.Api.Registration.Access;

/// <summary>Resolves an authenticated service key or a bound organization session to its Harvest caller.</summary>
/// <param name="broker">The registry, which knows each key's tenant.</param>
/// <param name="options">The console's explicit organization bindings and permission namespace.</param>
public sealed class CallerResolver(IRegistrationBroker broker, IOptions<HarvestConsoleOptions> options)
{
    /// <summary>Resolves the caller, or null when the principal has no live Harvest authority.</summary>
    /// <param name="user">The authenticated principal.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<Caller?> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var keyId = user.FindFirst(ApiKeyAuthenticationDefaults.KeyIdClaim)?.Value;
        if (keyId == ApiKeyConstants.OperatorKeyId)
            return new Caller(IsOperator: true, TenantId: null) { Scopes = HarvestScopeConstants.All };
        if (keyId is not null)
        {
            if (!Guid.TryParse(keyId, out var id))
                return null;
            return await broker.GetApiKeyAsync(id, cancellationToken) is Result<ApiKeyModel>.Success { Value: var key }
                ? new Caller(IsOperator: false, TenantId: key.TenantId) { Scopes = key.Scopes }
                : null;
        }

        var console = options.Value;
        if (!console.IsIdentityEnabled || user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirstValue(OrganizationClaimTypeConstants.Id), out var organizationId)
            || user.GetOrganizationRole(organizationId) is null
            || !console.OrganizationTenants.TryGetValue(organizationId, out var tenantId)
            || await broker.GetTenantAsync(tenantId, cancellationToken) is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return null;

        // Organization administrators remain tenant administrators; registry-wide tenant/key administration stays operator-only.
        var scopes = HarvestScopeConstants.All.Where(scope => scope != HarvestScopeConstants.TenantsManage
            && user.HasOrganizationPermission(organizationId, console.PermissionNamespace + ":" + scope)).ToArray();
        return scopes.Length == 0 ? null : new Caller(IsOperator: false, TenantId: tenantId)
        {
            Scopes = scopes,
            IsIdentitySession = true,
        };
    }
}
