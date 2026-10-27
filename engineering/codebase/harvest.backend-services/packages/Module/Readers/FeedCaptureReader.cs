using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Reads RSS 2.0 and Atom feeds, retaining each item's own identity and content projection.</summary>
public sealed class FeedCaptureReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "feed";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document)
    {
        try { return ReaderSupport.Xml(ReaderSupport.Decode(document)).Root?.Name.LocalName is "rss" or "feed" or "RDF"; }
        catch (XmlException) { return false; }
    }

    /// <inheritdoc />
    public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var xml = ReaderSupport.Xml(ReaderSupport.Decode(document));
            if (xml.Root?.Name.LocalName is not ("rss" or "feed" or "RDF")) return Task.FromResult(ReaderSupport.Invalid("RSS or Atom"));
            var captures = new List<Capture>();
            var locale = xml.Root.Attribute(XNamespace.Xml + "lang")?.Value
                ?? xml.Descendants().FirstOrDefault(element => element.Name.LocalName == "language")?.Value;
            foreach (var item in xml.Descendants().Where(element => element.Name.LocalName is "item" or "entry"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? Value(params string[] names) => item.Elements().FirstOrDefault(element => names.Contains(element.Name.LocalName))?.Value;
                var link = item.Elements().FirstOrDefault(element => element.Name.LocalName == "link"
                    && (element.Attribute("rel") is null || element.Attribute("rel")?.Value == "alternate"));
                var url = ReaderSupport.Url(document.FinalUrl, link?.Attribute("href")?.Value ?? link?.Value);
                var identity = Value("guid", "id") ?? url;
                if (identity is null) continue;
                var absoluteId = Uri.TryCreate(identity, UriKind.Absolute, out var idUri) && idUri.Scheme is "http" or "https" or "urn"
                    ? identity : null;
                var origin = url ?? absoluteId;
                url ??= absoluteId is not null ? ReaderSupport.Url(document.FinalUrl, absoluteId) : null;
                url ??= document.FinalUrl.AbsoluteUri + "#item-" + Uri.EscapeDataString(identity);
                var title = ReaderSupport.HtmlText(Value("title"));
                var text = ReaderSupport.HtmlText(Value("encoded", "content", "description", "summary"));
                var published = Value("published", "pubDate", "date");
                var author = item.Elements().FirstOrDefault(element => element.Name.LocalName == "author");
                var authorName = author?.Elements().FirstOrDefault(element => element.Name.LocalName == "name")?.Value
                    ?? author?.Value ?? Value("creator");
                var media = item.Descendants().Where(element => element.Name.LocalName is "enclosure" or "thumbnail"
                        || element.Name.LocalName == "content" && element.Attribute("url") is not null)
                    .Select(element => new { Url = ReaderSupport.Url(document.FinalUrl, element.Attribute("url")?.Value), Type = element.Attribute("type")?.Value })
                    .Where(value => value.Url is not null).Select((value, index) => new CaptureMedia
                    {
                        Url = value.Url!, Position = index, ContentType = value.Type,
                        Kind = value.Type?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true ? MediaKind.Video : MediaKind.Image,
                    }).ToList();
                var node = JsonSerializer.SerializeToElement(new { reader = "feed", title, text, author = authorName, published, id = identity });
                var projection = JsonSerializer.Serialize(new { title, text, published, author = authorName, media = media.Select(value => value.Url) });
                captures.Add(ReaderSupport.Capture(document, identity, url, projection, [node], origin, locale,
                    ReaderSupport.Timestamp(published), media, [new CaptureLink { Url = url }]));
            }
            // Most feeds are a moving window, so their missing entries do not prove deletion.
            return Task.FromResult(Result<CaptureReadResult>.Ok(new() { Captures = captures, Complete = false }));
        }
        catch (XmlException) { return Task.FromResult(ReaderSupport.Invalid("RSS or Atom")); }
    }
}
