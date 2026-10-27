using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.ApiKeys;

/// <summary>Answers core's API key scheme from the module's key table and the configured operator key.</summary>
/// <remarks>A tenant key is live while it is unrevoked, unexpired and its tenant is active; the operator key holds every
/// scope and is never touched.</remarks>
internal sealed class ModuleApiKeyRepository(HarvestDbContext db, IOptions<HarvestModuleOptions> options, TimeProvider time) : IApiKeyRepository
{
    /// <inheritdoc />
    public async Task<ApiKeyRecord?> FindLiveByHashAsync(string hash, CancellationToken cancellationToken)
    {
        if (options.Value.OperatorApiKey is { Length: > 0 } operatorKey
            && string.Equals(ApiKeySecretFactory.ToHash(operatorKey), hash, StringComparison.Ordinal))
        {
            return new ApiKeyRecord
            {
                Id = ApiKeyConstants.OperatorKeyId,
                Name = ApiKeyConstants.OperatorKeyId,
                LastUsedAt = time.GetUtcNow(),
                Scopes = HarvestScopeConstants.All,
            };
        }

        var now = time.GetUtcNow();
        var key = await (
                from row in db.ApiKeys.AsNoTracking()
                join tenant in db.Tenants.AsNoTracking() on row.TenantId equals tenant.Id
                where row.Hash == hash && row.RevokedAt == null && (row.ExpiresAt == null || row.ExpiresAt > now)
                      && tenant.State == TenantState.Active
                select row)
            .SingleOrDefaultAsync(cancellationToken);

        return key is null
            ? null
            : new ApiKeyRecord { Id = key.Id.ToString(), Name = key.Name, LastUsedAt = key.LastUsedAt, Scopes = key.Scopes };
    }

    /// <inheritdoc />
    public async Task TouchAsync(string id, DateTimeOffset usedAt, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var keyId))
            return;

        await db.ApiKeys.Where(row => row.Id == keyId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastUsedAt, usedAt.ToUniversalTime()), cancellationToken);
    }
}
