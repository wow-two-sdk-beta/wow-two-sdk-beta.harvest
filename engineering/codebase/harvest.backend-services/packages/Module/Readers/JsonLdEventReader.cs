using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Reads schema.org Event nodes from JSON-LD scripts or a JSON-LD response, including arrays and graphs.</summary>
public sealed class JsonLdEventReader : ICaptureReader
{
    /// <inheritdoc />
    public string Key => "jsonld-event";

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => ReadNodes(document).Nodes.Any(ReaderSupport.IsEvent);

    /// <inheritdoc />
    public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (nodes, warnings, locale, canonical) = ReadNodes(document);
        var events = nodes.Where(ReaderSupport.IsEvent).ToList();
        if (events.Count == 0) return Task.FromResult(ReaderSupport.Invalid("JSON-LD Event"));
        var captures = new List<Capture>();
        foreach (var node in events)
        {
            var rawId = ReaderSupport.String(node, "@id");
            var origin = rawId is not null && !rawId.StartsWith("_:", StringComparison.Ordinal)
                && Uri.TryCreate(document.FinalUrl, rawId, out var absoluteId) && absoluteId.Scheme is "http" or "https" or "urn"
                ? absoluteId.AbsoluteUri : null;
            var url = ReaderSupport.Url(document.FinalUrl, ReaderSupport.String(node, "url"))
                ?? ReaderSupport.Url(document.FinalUrl, origin) ?? canonical ?? document.FinalUrl.AbsoluteUri;
            var identity = (ReaderSupport.String(node, "name") ?? string.Empty) + "|" + ReaderSupport.String(node, "startDate");
            var synthetic = $"{url}#event-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)))[..24].ToLowerInvariant()}";
            var key = origin ?? (rawId?.StartsWith("_:", StringComparison.Ordinal) == true
                ? document.FinalUrl.AbsoluteUri + "#blank-" + Uri.EscapeDataString(rawId[2..])
                : events.Count == 1 ? url : synthetic);
            if (events.Count > 1 && ReaderSupport.String(node, "url") is null) url = origin is not null
                && Uri.TryCreate(origin, UriKind.Absolute, out var originUri) && originUri.Scheme is "http" or "https" ? origin : synthetic;
            captures.Add(ReaderSupport.Capture(document, key, url, ReaderSupport.Projection(node), [node], origin ?? (events.Count == 1 ? canonical : null),
                ReaderSupport.String(node, "inLanguage") ?? locale,
                ReaderSupport.Timestamp(ReaderSupport.String(node, "datePublished")), warnings: warnings));
        }
        return Task.FromResult(Result<CaptureReadResult>.Ok(new() { Captures = captures }));
    }

    private static (List<JsonElement> Nodes, List<string> Warnings, string? Locale, string? Canonical) ReadNodes(FetchedDocument document)
    {
        var body = ReaderSupport.Decode(document);
        var nodes = new List<JsonElement>();
        var warnings = new List<string>();
        string? locale = null;
        string? canonical = null;
        IEnumerable<string> scripts;
        if (body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('[')) scripts = [body];
        else
        {
            var html = ReaderSupport.Html(body);
            scripts = html.QuerySelectorAll("script[type='application/ld+json']").Select(element => element.TextContent);
            locale = html.DocumentElement?.GetAttribute("lang");
            canonical = ReaderSupport.Url(document.FinalUrl, html.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        }
        foreach (var script in scripts)
        {
            try
            {
                using var json = JsonDocument.Parse(script, new() { MaxDepth = 64 });
                nodes.AddRange(ReaderSupport.Nodes(json.RootElement).Select(node => node.Clone()));
            }
            catch (JsonException) { warnings.Add("malformed_jsonld"); }
        }
        return (nodes, warnings, locale, canonical);
    }
}
