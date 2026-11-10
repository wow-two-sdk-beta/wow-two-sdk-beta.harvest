using System.Security.Claims;
using Harvest.Api.Console;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Identity.Core.Organizations;

namespace Harvest.Api.Registration.Access;

/// <summary>Resolves authenticated service credentials or browser sessions to an SCFD operator or tenant.</summary>
/// <param name="broker">The tenant and key registry.</param>
/// <param name="options">The service issuer and explicit client grants.</param>
/// <param name="console">The browser session's operator and organization bindings.</param>
public sealed class CallerResolver(
    IRegistrationBroker broker,
    IOptions<HarvestServiceAccessOptions> options,
    IOptions<HarvestConsoleOptions> console)
{
    /// <summary>Resolves the caller, or null when the principal has no live Harvest authority.</summary>
    /// <param name="user">The authenticated principal.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<Caller?> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (console.Value.IsIdentityEnabled && user.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated
            && candidate.AuthenticationType == BffAuthenticationSchemeConstants.Cookie) is { } browser)
            return await ResolveBrowserAsync(browser, cancellationToken);

        if (options.Value.Enabled)
            return await ResolveServiceAsync(user, cancellationToken);

        var identity = user.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated
            && candidate.AuthenticationType == ApiKeyAuthenticationDefaults.Scheme);
        var keyId = identity?.FindFirst(ApiKeyAuthenticationDefaults.KeyIdClaim)?.Value;
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

        return null;
    }

    private async Task<Caller?> ResolveBrowserAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        var settings = console.Value;
        var subject = identity.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return null;
        if (settings.OperatorSubjectIds.Contains(subject, StringComparer.Ordinal))
            return new Caller(IsOperator: true, TenantId: null)
            {
                Scopes = HarvestScopeConstants.All,
                IsIdentitySession = true,
            };

        var principal = new ClaimsPrincipal(identity);
        if (!Guid.TryParse(identity.FindFirst(OrganizationClaimTypeConstants.Id)?.Value, out var organizationId)
            || principal.GetOrganizationRole(organizationId) is null
            || !settings.OrganizationTenants.TryGetValue(organizationId.ToString("D"), out var tenantId)
            || await broker.GetTenantAsync(tenantId, cancellationToken)
                is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return null;

        var scopes = HarvestScopeConstants.All.Where(scope => scope != HarvestScopeConstants.TenantsManage
            && principal.HasOrganizationPermission(organizationId, settings.PermissionNamespace + ":" + scope)).ToArray();
        return scopes.Length == 0 ? null : new Caller(IsOperator: false, TenantId: tenantId)
        {
            Scopes = scopes,
            IsIdentitySession = true,
        };
    }

    private async Task<Caller?> ResolveServiceAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var identity = user.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated
            && candidate.AuthenticationType == HarvestServiceAccessOptions.Scheme);
        var clientId = identity?.FindFirst("client_id")?.Value;
        if (identity is null || clientId is null || clientId != identity.FindFirst("sub")?.Value
            || identity.HasClaim(claim => claim.Type is "sid" or "act")
            || !options.Value.ClientGrants.TryGetValue(clientId, out var grant))
            return null;

        var tokenScopes = identity.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        // Identity resource scopes use a dot. Keep the registry's existing API-key scope names.
        var scopes = grant.Scopes.Where(scope => tokenScopes.Contains(scope.Replace(':', '.'))).ToArray();
        if (scopes.Length == 0 || grant.TenantId is { } tenantId
            && await broker.GetTenantAsync(tenantId, cancellationToken)
                is not Result<TenantModel>.Success { Value.State: TenantState.Active })
            return null;

        return new Caller(grant.IsOperator, grant.TenantId) { Scopes = scopes };
    }
}
