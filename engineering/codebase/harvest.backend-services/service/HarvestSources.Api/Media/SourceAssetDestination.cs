using System.Runtime.CompilerServices;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

[assembly: InternalsVisibleTo("HarvestSources.Tests.E2E")]

namespace HarvestSources.Api.Media;

/// <summary>Restricts web copies to their source host and official Instagram images to Meta's media CDNs.
/// The guarded fetcher still checks public IPs, redirects and host pacing; web images also obey robots rules.</summary>
internal sealed class SourceAssetDestination(string host, bool instagram) : IFetchDestinationPolicy
{
    public bool Allows(Uri destination) => destination.Scheme == Uri.UriSchemeHttps
        && destination.IsDefaultPort && destination.UserInfo.Length == 0
        && (instagram
            ? !destination.Query.Contains("access_token", StringComparison.OrdinalIgnoreCase)
                && (destination.IdnHost.EndsWith(".cdninstagram.com", StringComparison.OrdinalIgnoreCase)
                    || destination.IdnHost.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase))
            : string.Equals(destination.IdnHost, host, StringComparison.OrdinalIgnoreCase));
}
