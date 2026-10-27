namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Fetches public pages directly under shared host pacing, robots and conditional-request policies.</summary>
public interface IPageFetcher
{
    /// <summary>Reads a page or reports why the source could not be read.</summary>
    /// <param name="request">The URL and source policy.</param>
    /// <param name="cancellationToken">Cancels the host wait and network work.</param>
    /// <returns>The response and classified outcome.</returns>
    Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default);
}
