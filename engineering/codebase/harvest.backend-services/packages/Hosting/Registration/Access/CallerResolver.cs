using System.Security.Claims;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Harvest.Api.Registration.Access;

/// <summary>Resolves service credentials to a Harvest operator or tenant.</summary>
/// <param name="broker">The tenant and key registry.</param>
/// <param name="options">The service issuer and explicit client grants.</param>
public sealed class CallerResolver(IRegistrationBroker broker, IOptions<HarvestServiceAccessOptions> options)
{
    /// <summary>Resolves the caller, or null when the principal has no live Harvest authority.</summary>
    /// <param name="user">The authenticated principal.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<Caller?> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
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
