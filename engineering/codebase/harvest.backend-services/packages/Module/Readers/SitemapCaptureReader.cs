using System.Xml;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Reads sitemap URL sets and indexes into a frontier without treating their addresses as posts.</summary>
public sealed class SitemapCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "sitemap";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document)
    {
        try { return ReaderSupport.Xml(ReaderSupport.Decode(document)).Root?.Name.LocalName is "urlset" or "sitemapindex"; }
        catch (XmlException) { return false; }
    }

    /// <inheritdoc />
    public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var xml = ReaderSupport.Xml(ReaderSupport.Decode(document));
            var name = xml.Root?.Name.LocalName;
            if (name is not ("urlset" or "sitemapindex")) return Task.FromResult(ReaderSupport.Invalid("sitemap"));
            var discovered = new List<DiscoveredUrl>();
            var complete = true;
            foreach (var entry in xml.Root!.Elements())
            {
                var raw = entry.Elements().FirstOrDefault(element => element.Name.LocalName == "loc")?.Value;
                if (!Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var absolute)
                    || ReaderSupport.Url(document.FinalUrl, absolute.AbsoluteUri) is not { } url)
                { complete = false; continue; }
                discovered.Add(new DiscoveredUrl
                {
                    Url = url, IsListing = name == "sitemapindex",
                    LastModifiedUtc = ReaderSupport.Timestamp(entry.Elements().FirstOrDefault(element => element.Name.LocalName == "lastmod")?.Value),
                });
            }
            return Task.FromResult(Result<CaptureReadResult>.Ok(new() { Discovered = discovered.DistinctBy(value => value.Url).ToList(), Complete = complete }));
        }
        catch (XmlException) { return Task.FromResult(ReaderSupport.Invalid("sitemap")); }
    }
}
