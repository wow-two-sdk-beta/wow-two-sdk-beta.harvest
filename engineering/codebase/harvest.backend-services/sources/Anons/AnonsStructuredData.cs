using System.Text.Json;
using AngleSharp.Dom;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;

internal static class AnonsStructuredData
{
    internal static JsonElement? Event(IDocument html)
    {
        foreach (IElement script in html.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using JsonDocument data = JsonDocument.Parse(script.TextContent, new JsonDocumentOptions { MaxDepth = 64 });
                if (FindEvent(data.RootElement) is { } node)
                    return node.Clone();
            }
            catch (JsonException) { /* Other metadata blocks can still contain a valid Event. */ }
        }
        return null;
    }

    internal static string? String(JsonElement? node, string property) => node is { ValueKind: JsonValueKind.Object } value
        && value.TryGetProperty(property, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static JsonElement? FindEvent(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
                if (FindEvent(item) is { } found)
                    return found;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("@type", out JsonElement type)
                && (type.ValueKind == JsonValueKind.String && type.GetString() == "Event"
                    || type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "Event")))
                return node;
            if (node.TryGetProperty("@graph", out JsonElement graph))
                return FindEvent(graph);
        }
        return null;
    }
}
