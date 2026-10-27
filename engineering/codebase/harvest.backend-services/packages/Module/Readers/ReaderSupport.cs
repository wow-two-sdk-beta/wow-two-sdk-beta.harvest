using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Shares deterministic, I/O-free parsing and capture normalization between the family readers.</summary>
internal static partial class ReaderSupport
{
    internal static string Decode(FetchedDocument document)
    {
        Encoding encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(document.Charset))
        {
            try { encoding = Encoding.GetEncoding(document.Charset.Trim('"', '\'')); }
            catch (ArgumentException) { /* An unknown charset falls back to UTF-8. */ }
        }
        return encoding.GetString(document.Body.Span).TrimStart('\uFEFF');
    }

    internal static IDocument Html(string html) => new HtmlParser().ParseDocument(html);

    internal static XDocument Xml(string text)
    {
        using var input = new StringReader(text);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16 * 1024 * 1024,
        });
        return XDocument.Load(reader);
    }

    internal static string Text(string? text) => Whitespace().Replace(text ?? string.Empty, " ").Trim().Normalize(NormalizationForm.FormC);

    internal static string HtmlText(string? html)
    {
        var document = Html(html ?? string.Empty);
        foreach (var element in document.QuerySelectorAll("script,style,noscript,template,nav,aside"))
            element.Remove();
        return ElementText(document.QuerySelector("article,main") ?? document.Body);
    }

    internal static string ElementText(INode? node) => node is null ? string.Empty : Text(string.Join(' ', TextNodes(node)));

    private static IEnumerable<string> TextNodes(INode node)
    {
        if (node is IText text) yield return text.Data;
        else
            foreach (var child in node.ChildNodes)
                foreach (var value in TextNodes(child)) yield return value;
    }

    internal static string Projection(JsonElement node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteProjection(writer, node);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteProjection(Utf8JsonWriter writer, JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in node.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (property.Name is "interactionStatistic" or "aggregateRating" or "commentCount" or "viewCount" or "likeCount") continue;
                writer.WritePropertyName(property.Name);
                WriteProjection(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var value in node.EnumerateArray()) WriteProjection(writer, value);
            writer.WriteEndArray();
        }
        else node.WriteTo(writer);
    }

    internal static string? Url(Uri basis, string? raw, bool stripTracking = true)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(basis, raw.Trim(), out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return null;
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        if (stripTracking && builder.Query.Length > 1)
        {
            builder.Query = string.Join('&', builder.Query[1..].Split('&').Where(part =>
            {
                var key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("fbclid", StringComparison.OrdinalIgnoreCase)
                    && !key.Equals("gclid", StringComparison.OrdinalIgnoreCase);
            }).Order(StringComparer.Ordinal));
        }
        return builder.Uri.AbsoluteUri;
    }

    internal static DateTimeOffset? Timestamp(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime() : null;

    internal static Capture Capture(FetchedDocument document, string externalKey, string url, string projection,
        IReadOnlyList<JsonElement>? structured = null, string? origin = null, string? locale = null,
        DateTimeOffset? published = null, IReadOnlyList<CaptureMedia>? media = null, IReadOnlyList<CaptureLink>? links = null,
        IReadOnlyList<string>? warnings = null) => new()
    {
        ExternalKey = externalKey,
        Url = url,
        RequestUrl = document.RequestUrl.AbsoluteUri,
        FinalUrl = document.FinalUrl.AbsoluteUri,
        HttpStatus = document.StatusCode,
        CapturedAtUtc = document.FetchedAtUtc,
        ContentType = document.ContentType,
        Body = document.Body,
        ContentProjection = projection,
        Structured = structured ?? [],
        OriginKey = origin,
        Locale = locale,
        PublishedAtUtc = published,
        Media = media ?? [],
        Links = links ?? [],
        Warnings = warnings ?? [],
    };

    internal static Result<CaptureReadResult> Invalid(string format) =>
        Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed($"The document is not valid {format}."));

    internal static string? String(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(key, out var value) ? Scalar(value) : null;

    internal static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    internal static bool IsEvent(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("@type", out var type)) return false;
        return (type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(Scalar) : [Scalar(type)])
            .Any(name => name is not null && (name.Equals("Event", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Event", StringComparison.Ordinal) || name.EndsWith("/Event", StringComparison.Ordinal)));
    }

    internal static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray())
                foreach (var nested in Nodes(child)) yield return nested;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            if (node.TryGetProperty("@graph", out var graph))
                foreach (var nested in Nodes(graph)) yield return nested;
        }
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
