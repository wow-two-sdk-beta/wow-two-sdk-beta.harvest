using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Reads one format of fetched document — RSS, ICS, JSON-LD, a sitemap, a link card — into captures.</summary>
/// <remarks>Generic family readers stay public in Harvest; named-source brokers compose them. A reader does no I/O of
/// its own: the step fetches, the reader parses.</remarks>
public interface ICaptureReader
{
    /// <summary>Gets the reader's stable key, such as <c>rss</c> or <c>jsonld-event</c>.</summary>
    string Key { get; }

    /// <summary>Tells whether the reader understands the document.</summary>
    /// <param name="document">The fetched document.</param>
    /// <returns><c>true</c> when the reader can read it.</returns>
    bool CanRead(FetchedDocument document);

    /// <summary>Reads the document.</summary>
    /// <param name="document">The fetched document.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The captures and addresses; <c>SerializationFailed</c> when the document is not the reader's format.</returns>
    Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default);
}
