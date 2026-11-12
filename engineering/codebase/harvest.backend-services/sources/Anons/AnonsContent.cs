using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;

internal static class AnonsContent
{
    internal static IElement? Read(IDocument html)
    {
        IElement? source = html.QuerySelector(".section-container .section-content.content, article .content");
        if (source is null)
            return null;
        var content = (IElement)source.Clone();
        foreach (IElement editor in content.QuerySelectorAll("[data-editorjs]"))
        {
            try
            {
                using JsonDocument data = JsonDocument.Parse(editor.GetAttribute("data-editorjs") ?? string.Empty,
                    new JsonDocumentOptions { MaxDepth = 64 });
                if (data.RootElement.ValueKind != JsonValueKind.Object || !data.RootElement.TryGetProperty("blocks", out JsonElement blocks)
                    || blocks.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement block in blocks.EnumerateArray())
                    AppendBlock(editor, block);
            }
            catch (JsonException) { /* Keep any server-rendered fallback content. */ }
            editor.RemoveAttribute("data-editorjs");
        }
        return content;
    }

    private static void AppendBlock(IElement editor, JsonElement block)
    {
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            return;
        foreach (string fragment in Fragments(data))
        {
            IElement paragraph = editor.Owner!.CreateElement("p");
            foreach (INode node in new HtmlParser().ParseFragment(fragment, paragraph).ToArray())
                paragraph.AppendChild(node);
            editor.AppendChild(paragraph);
        }
        if (AnonsStructuredData.String(block, "type") == "image" && data.TryGetProperty("file", out JsonElement file)
            && AnonsStructuredData.String(file, "url") is { } image)
        {
            IElement element = editor.Owner!.CreateElement("img");
            element.SetAttribute("src", image);
            element.SetAttribute("alt", AnonsStructuredData.String(data, "caption") ?? string.Empty);
            editor.AppendChild(element);
        }
        foreach (string key in new[] { "link", "source" })
            if (AnonsStructuredData.String(data, key) is { } url)
            {
                IElement anchor = editor.Owner!.CreateElement("a");
                anchor.SetAttribute("href", url);
                anchor.TextContent = AnonsStructuredData.String(data, "title") ?? url;
                editor.AppendChild(anchor);
            }
    }

    private static IEnumerable<string> Fragments(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            yield return node.GetString()!;
            yield break;
        }
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in node.EnumerateArray())
                foreach (string text in Fragments(item))
                    yield return text;
            yield break;
        }
        if (node.ValueKind != JsonValueKind.Object)
            yield break;
        foreach (string property in new[] { "text", "content", "caption", "title", "message", "items" })
            if (node.TryGetProperty(property, out JsonElement value))
                foreach (string text in Fragments(value))
                    yield return text;
    }
}
