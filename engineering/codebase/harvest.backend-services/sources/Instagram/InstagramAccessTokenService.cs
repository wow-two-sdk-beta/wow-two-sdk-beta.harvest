using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Integrations;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Supplies only this runtime's Instagram credential to the explicitly qualified provider client.</summary>
internal sealed class InstagramAccessTokenService(IOptionsMonitor<InstagramOptions> configuration) : IAccessTokenService
{
    public Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        InstagramOptions options = configuration.CurrentValue;
        return Task.FromResult(InstagramConfiguration.IsValid(options) ? options.AccessToken : null);
    }
}
