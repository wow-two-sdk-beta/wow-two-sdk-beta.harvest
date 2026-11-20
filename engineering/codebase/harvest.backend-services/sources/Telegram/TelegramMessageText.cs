using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

/// <summary>Turns a message's HTML body into plain text and keeps its links: each web link stays in the text beside
/// its label and joins the capture's links; hashtag and mention links add nothing.</summary>
internal static partial class TelegramMessageText
{
    private const int MaxLinks = 50;

    /// <summary>Reads the text of one message body and the web links it carries.</summary>
    /// <param name="body">The message's text element.</param>
    /// <param name="basis">The page the message was read from, which relative links resolve against.</param>
    /// <param name="links">Receives the message's web links, once each, in reading order.</param>
    internal static string Read(IElement body, Uri basis, List<CaptureLink> links)
    {
        var text = new StringBuilder();
        Append(body, basis, text, links);
        string[] lines = [.. text.ToString().Replace(' ', ' ').Replace("\r", string.Empty).Split('\n').Select(line => line.TrimEnd())];
        return BlankRuns().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    private static void Append(INode node, Uri basis, StringBuilder text, List<CaptureLink> links)
    {
        switch (node)
        {
            case IText run:
                text.Append(Spaces().Replace(run.Data, " "));
                break;
            case IElement { LocalName: "br" }:
                text.Append('\n');
                break;
            case IElement { LocalName: "script" or "style" }:
                break;
            case IElement { LocalName: "a" } anchor:
                AppendLink(anchor, basis, text, links);
                break;
            case IElement { LocalName: "blockquote" or "pre" or "div" or "p" or "ul" or "ol" or "li" } block:
                EndLine(text);
                AppendChildren(block, basis, text, links);
                EndLine(text);
                break;
            case IElement element:
                AppendChildren(element, basis, text, links);
                break;
        }
    }

    private static void AppendChildren(IElement element, Uri basis, StringBuilder text, List<CaptureLink> links)
    {
        foreach (INode child in element.ChildNodes)
            Append(child, basis, text, links);
    }

    private static void AppendLink(IElement anchor, Uri basis, StringBuilder text, List<CaptureLink> links)
    {
        int start = text.Length;
        AppendChildren(anchor, basis, text, links);
        string label = text.ToString(start, text.Length - start).Trim();
        if (WebLink(anchor.GetAttribute("href"), basis) is not { } url)
            return;
        if (links.Count < MaxLinks && !links.Any(link => link.Url == url))
            links.Add(new() { Url = url, Text = label.Length > 0 ? label : null });
        if (label.Length > 0 && label[0] is not ('#' or '@') && !Names(label, url))
            text.Append(" (").Append(url).Append(')');
    }

    /// <summary>Resolves a link to a public web address, leaving out the preview page's own hashtag searches.</summary>
    private static string? WebLink(string? href, Uri basis)
    {
        if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(basis, href.Trim(), out Uri? url)
            || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
            return null;
        if (url.IdnHost.Equals(TelegramChannels.Host, StringComparison.OrdinalIgnoreCase)
            && url.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal) && url.Query.StartsWith("?q=", StringComparison.Ordinal))
            return null;
        return new UriBuilder(url) { Fragment = string.Empty }.Uri.AbsoluteUri;
    }

    /// <summary>Tells whether a link's label already is its address: the same without the scheme or a trailing slash, or
    /// the address cut short by an ellipsis. A word such as a site's name is not.</summary>
    private static bool Names(string label, string url)
    {
        static string Bare(string value) => SchemePrefix().Replace(value.Trim(), string.Empty).TrimEnd('…', '.', '/').ToLowerInvariant();
        string shown = Bare(label);
        string address = Bare(url);
        bool cutShort = label.TrimEnd().EndsWith('…') || label.TrimEnd().EndsWith("...", StringComparison.Ordinal);
        return shown.Length > 0 && (shown == address || cutShort && address.StartsWith(shown, StringComparison.Ordinal));
    }

    private static void EndLine(StringBuilder text)
    {
        if (text.Length > 0 && text[^1] != '\n')
            text.Append('\n');
    }

    [GeneratedRegex("[ \\t\\r\\n\\f]+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex("\\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex BlankRuns();

    [GeneratedRegex("^(?:https?://)?(?:www\\.)?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SchemePrefix();
}
