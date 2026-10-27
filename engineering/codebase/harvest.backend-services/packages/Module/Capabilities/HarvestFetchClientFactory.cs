using System.Net;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Creates isolated direct clients over core's guarded, proxy-free transport.</summary>
internal sealed class HarvestFetchClientFactory(
    IHttpClientFactory clients,
    IHostFetchStore hosts,
    IOptions<HarvestFetchOptions> options,
    IChallengeDetector challenges,
    TimeProvider clock) : IFetchClientFactory
{
    internal const string TransportName = "Harvest.Direct";

    public HttpClient CreateClient(CookieContainer? cookies = null) => new(
        new HarvestFetchHandler(clients.CreateClient(TransportName), hosts, options.Value, challenges, clock, cookies), disposeHandler: true)
    {
        Timeout = options.Value.Timeout
    };
}
