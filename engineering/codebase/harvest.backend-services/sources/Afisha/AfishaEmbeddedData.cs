using System.Globalization;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

/// <summary>Reads literal data shipped in Afisha's HTML without executing scripts or requesting its API.</summary>
internal static class AfishaEmbeddedData
{
    private const string FlightPrefix = "self.__next_f.push(";

    internal static JsonElement? Material(IDocument html)
    {
        byte[] stream = Encoding.UTF8.GetBytes(FlightStream(html));
        int offset = 0;
        while (offset < stream.Length)
        {
            int end = Array.IndexOf(stream, (byte)'\n', offset);
            if (end < 0)
                end = stream.Length;
            int colon = Array.IndexOf(stream, (byte)':', offset, end - offset);
            if (colon < 0 || colon + 1 == end)
            {
                offset = end + 1;
                continue;
            }
            if (stream[colon + 1] == 'T' && TextRecord(stream, colon + 2) is { } text)
            {
                offset = text.End;
                continue;
            }
            try
            {
                using JsonDocument payload = JsonDocument.Parse(stream.AsMemory(colon + 1, end - colon - 1), new() { MaxDepth = 64 });
                if (FindMaterial(payload.RootElement) is { } material)
                    return material;
            }
            catch (JsonException) { /* Other Flight rows are not JSON data. */ }
            offset = end + 1;
        }
        return null;
    }

    internal static string? Content(IDocument html, JsonElement? material)
    {
        string? value = String(material, "content");
        if (value is null || !value.StartsWith('$'))
            return value;
        if (value.StartsWith("$$", StringComparison.Ordinal))
            return value[1..];
        string id = value[1..];
        if (id.Length == 0 || !id.All(char.IsAsciiHexDigit))
            return null;
        byte[] stream = Encoding.UTF8.GetBytes(FlightStream(html));
        int offset = 0;
        while (offset < stream.Length)
        {
            int end = Array.IndexOf(stream, (byte)'\n', offset);
            if (end < 0)
                end = stream.Length;
            int colon = Array.IndexOf(stream, (byte)':', offset, end - offset);
            if (colon >= 0 && colon + 1 < end && stream[colon + 1] == 'T'
                && TextRecord(stream, colon + 2) is { } text)
            {
                if (Encoding.UTF8.GetString(stream, offset, colon - offset) == id)
                    return Encoding.UTF8.GetString(stream, text.Start, text.End - text.Start);
                offset = text.End;
            }
            else
            {
                // Short strings are ordinary JSON rows rather than length-delimited text rows.
                if (colon >= 0 && Encoding.UTF8.GetString(stream, offset, colon - offset) == id)
                {
                    try
                    {
                        using JsonDocument payload = JsonDocument.Parse(stream.AsMemory(colon + 1, end - colon - 1));
                        return payload.RootElement.ValueKind == JsonValueKind.String ? payload.RootElement.GetString() : null;
                    }
                    catch (JsonException) { return null; }
                }
                offset = end + 1;
            }
        }
        return null;
    }

    private static string FlightStream(IDocument html)
    {
        var stream = new StringBuilder();
        foreach (IElement script in html.QuerySelectorAll("script"))
        {
            string source = script.TextContent.Trim().TrimEnd(';');
            if (!source.StartsWith(FlightPrefix, StringComparison.Ordinal) || !source.EndsWith(')'))
                continue;
            try
            {
                using JsonDocument push = JsonDocument.Parse(source[FlightPrefix.Length..^1], new() { MaxDepth = 64 });
                if (push.RootElement.ValueKind == JsonValueKind.Array && push.RootElement.GetArrayLength() >= 2
                    && push.RootElement[0].ValueKind == JsonValueKind.Number
                    && push.RootElement[0].TryGetInt32(out int channel) && channel == 1
                    && push.RootElement[1].ValueKind == JsonValueKind.String)
                    stream.Append(push.RootElement[1].GetString());
            }
            catch (JsonException) { /* A future script wrapper must not execute or break ordinary JSON-LD. */ }
        }
        return stream.ToString();
    }

    private static (int Start, int End)? TextRecord(byte[] stream, int offset)
    {
        int comma = Array.IndexOf(stream, (byte)',', offset);
        if (comma < 0 || comma - offset is < 1 or > 8
            || !int.TryParse(Encoding.ASCII.GetString(stream, offset, comma - offset), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out int length)
            || length < 0 || length > stream.Length - comma - 1)
            return null;
        // Flight declares UTF-8 byte lengths, not UTF-16 character counts; Russian text differs substantially.
        return (comma + 1, comma + 1 + length);
    }

    internal static JsonElement? Event(IDocument html)
    {
        foreach (IElement script in html.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(script.TextContent, new() { MaxDepth = 64 });
                if (FindEvent(document.RootElement) is { } node)
                    return node;
            }
            catch (JsonException) { /* A malformed optional node leaves source dates unknown. */ }
        }
        return null;
    }

    internal static string? String(JsonElement? element, string name)
    {
        return element is { ValueKind: JsonValueKind.Object } node && node.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static JsonElement? FindMaterial(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("initialData", out JsonElement initial)
                && String(initial, "@type") == "Material")
                return initial.Clone();
            foreach (JsonProperty property in node.EnumerateObject())
                if (FindMaterial(property.Value) is { } found)
                    return found;
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in node.EnumerateArray())
                if (FindMaterial(child) is { } found)
                    return found;
        return null;
    }

    private static JsonElement? FindEvent(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            if (node.TryGetProperty("@type", out JsonElement type)
                && (type.ValueKind == JsonValueKind.String && type.GetString() == "Event"
                    || type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "Event")))
                return node.Clone();
            if (node.TryGetProperty("@graph", out JsonElement graph))
                return FindEvent(graph);
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (JsonElement child in node.EnumerateArray())
                if (FindEvent(child) is { } found)
                    return found;
        return null;
    }
}
