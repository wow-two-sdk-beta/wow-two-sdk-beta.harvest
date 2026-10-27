using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Interprets a source's successful HTTP response as availability evidence for a known link.</summary>
public interface ILinkAvailabilityReader
{
    /// <summary>Tells whether this reader checks the requested source, including redirects and unexpected content.</summary>
    /// <param name="document">The response being checked.</param>
    /// <returns>Whether this source's availability rules apply.</returns>
    bool CanCheck(FetchedDocument document);

    /// <summary>Checks identity and source markers; an unrecognized response returns null rather than a positive.</summary>
    /// <param name="document">The fetched response.</param>
    /// <param name="externalKey">The known source-local link identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Explicit availability evidence, or null when the result is inconclusive.</returns>
    Task<LinkObservation?> ReadAvailabilityAsync(
        FetchedDocument document,
        string externalKey,
        CancellationToken cancellationToken = default);
}
