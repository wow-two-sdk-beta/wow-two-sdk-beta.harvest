using System.Runtime.CompilerServices;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

[assembly: InternalsVisibleTo("HarvestSources.Tests.E2E")]

namespace HarvestSources.Api.Media;

/// <summary>Restricts web copies to their source host and the image domains its adapter declares, and official Instagram
/// images to Meta's media CDNs. A declared domain admits its own host and every subdomain, never a look-alike such as
/// <c>evilolxcdn.com</c> or <c>olxcdn.com.evil.test</c>.
/// The guarded fetcher still checks public IPs, redirects and host pacing; web images also obey robots rules.</summary>
internal sealed class SourceAssetDestination(string host, bool instagram, IReadOnlyList<string>? domains = null) : IFetchDestinationPolicy
{
    public bool Allows(Uri destination) => destination.Scheme == Uri.UriSchemeHttps
        && destination.IsDefaultPort && destination.UserInfo.Length == 0
        && (instagram
            ? !destination.Query.Contains("access_token", StringComparison.OrdinalIgnoreCase)
                && (destination.IdnHost.EndsWith(".cdninstagram.com", StringComparison.OrdinalIgnoreCase)
                    || destination.IdnHost.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase))
            : string.Equals(destination.IdnHost, host, StringComparison.OrdinalIgnoreCase) || OnDeclaredDomain(destination.IdnHost));

    /// <summary>Tells whether a host is a declared domain or a subdomain of one.</summary>
    private bool OnDeclaredDomain(string candidate) => domains?.Any(domain =>
        string.Equals(candidate, domain, StringComparison.OrdinalIgnoreCase)
        || candidate.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) == true;
}
