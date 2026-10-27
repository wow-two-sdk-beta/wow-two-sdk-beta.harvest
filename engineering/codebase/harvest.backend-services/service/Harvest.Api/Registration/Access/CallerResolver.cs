using System.Security.Claims;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Harvest.Api.Registration.Access;

/// <summary>Resolves the authenticated key to the operator or its tenant.</summary>
/// <param name="broker">The registry, which knows each key's tenant.</param>
public sealed class CallerResolver(IRegistrationBroker broker)
{
    /// <summary>Resolves the caller, or null when the principal carries no live Harvest key.</summary>
    /// <param name="user">The authenticated principal.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<Caller?> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var keyId = user.FindFirst(ApiKeyAuthenticationDefaults.KeyIdClaim)?.Value;
        if (keyId == ApiKeyConstants.OperatorKeyId)
            return new Caller(IsOperator: true, TenantId: null);
        if (!Guid.TryParse(keyId, out var id))
            return null;

        return await broker.GetApiKeyAsync(id, cancellationToken) is Result<ApiKeyModel>.Success { Value: var key }
            ? new Caller(IsOperator: false, TenantId: key.TenantId)
            : null;
    }
}
