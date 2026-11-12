using System.Text;
using System.Text.Json;
using AngleSharp.Dom;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

/// <summary>Reads OLX's literal structured data without running JavaScript or calling private APIs.</summary>
internal static class OlxEmbeddedData
{
    internal static JsonElement? Ad(IDocument document)
    {
        foreach (IElement script in document.QuerySelectorAll("script"))
        {
            string source = script.TextContent;
            int marker = source.IndexOf("window.__PRERENDERED_STATE__", StringComparison.Ordinal);
            if (marker < 0) continue;
            int equals = source.IndexOf('=', marker);
            if (equals < 0) continue;
            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(source[(equals + 1)..].TrimStart()), new() { MaxDepth = 64 });
                using JsonDocument literal = JsonDocument.ParseValue(ref reader);
                using JsonDocument state = JsonDocument.Parse(literal.RootElement.ValueKind == JsonValueKind.String
                    ? literal.RootElement.GetString()! : literal.RootElement.GetRawText(), new() { MaxDepth = 64 });
                if (state.RootElement.ValueKind == JsonValueKind.Object && state.RootElement.TryGetProperty("ad", out JsonElement holder)
                    && holder.ValueKind == JsonValueKind.Object && holder.TryGetProperty("ad", out JsonElement ad) && ad.ValueKind == JsonValueKind.Object)
                    return ad.Clone();
            }
            catch (JsonException) { /* An optional hydration shape must not prevent DOM/JSON-LD extraction. */ }
        }
        return null;
    }

    internal static JsonElement? Product(IDocument document, string externalKey)
    {
        foreach (IElement script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(script.TextContent, new() { MaxDepth = 64 });
                foreach (JsonElement node in Nodes(json.RootElement))
                {
                    if (!ProductType(node)) continue;
                    string? url = Text(node, "url");
                    if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out Uri? identity)
                        || OlxUrls.AdKey(identity) != externalKey)) continue;
                    return node.Clone();
                }
            }
            catch (JsonException) { /* Visible listing fields can survive malformed optional metadata. */ }
        }
        return null;
    }

    internal static string? Text(JsonElement? node, string property)
    {
        if (node is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty(property, out JsonElement item)) return null;
        return item.ValueKind is JsonValueKind.String ? item.GetString()
            : item.ValueKind is JsonValueKind.Number ? item.GetRawText() : null;
    }

    private static bool ProductType(JsonElement node)
    {
        if (!node.TryGetProperty("@type", out JsonElement type)) return false;
        return type.ValueKind == JsonValueKind.String && type.GetString() == "Product"
            || type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "Product");
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in node.EnumerateArray())
                foreach (JsonElement nested in Nodes(child)) yield return nested;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            if (node.TryGetProperty("@graph", out JsonElement graph))
                foreach (JsonElement nested in Nodes(graph)) yield return nested;
        }
    }
}
