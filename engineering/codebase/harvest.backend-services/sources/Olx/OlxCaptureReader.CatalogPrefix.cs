using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Html.Parser.Tokens;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

public sealed partial class OlxCaptureReader
{
    private const string CatalogForm = "form[data-testid='listing-filters-form']";
    private const string ListingGrid = "div[data-testid='listing-grid']";
    private const string PaginationWrapper = "div[data-testid='pagination-wrapper']";
    private const string PaginationList = "ul[data-testid='pagination-list']";

    /// <inheritdoc />
    public bool AllowsPrefix(Uri destination) => OlxUrls.IsPublic(destination) && OlxUrls.AdKey(destination) is null;

    /// <inheritdoc />
    public bool ShouldStopReading(Uri destination, ReadOnlySpan<byte> prefix, int previousLength)
    {
        // OLX closes the pagination list and its wrapper together. Parse only when a new possible boundary arrives.
        ReadOnlySpan<byte> closingTags = "</ul></div>"u8;
        int newMarkup = Math.Max(0, previousLength - closingTags.Length + 1);
        if (prefix[newMarkup..].IndexOf(closingTags) < 0 || prefix.IndexOf("pagination-forward"u8) < 0)
            return false;

        int depth = 0;
        int gridDepth = -1, wrapperDepth = -1;
        int grids = 0, wrappers = 0;
        bool gridClosed = false, wrapperClosed = false;
        var parser = new HtmlParser(new HtmlParserOptions
        {
            // The DOM alone repairs missing close tags at EOF. Require explicit closes recognized by the HTML tokenizer.
            OnToken = (token, _) =>
            {
                if (token is not HtmlTagToken { Name: "div" } tag) return;
                if (tag.Type == HtmlTokenType.StartTag)
                {
                    depth++;
                    switch (tag.GetAttribute("data-testid"))
                    {
                        case "listing-grid":
                            grids++;
                            gridDepth = depth;
                            break;
                        case "pagination-wrapper":
                            wrappers++;
                            if (gridClosed) wrapperDepth = depth;
                            break;
                    }
                }
                else if (tag.Type == HtmlTokenType.EndTag)
                {
                    gridClosed |= grids == 1 && depth == gridDepth;
                    wrapperClosed |= wrappers == 1 && depth == wrapperDepth;
                    depth--;
                }
            }
        });
        IDocument html = parser.ParseDocument(Encoding.UTF8.GetString(prefix).TrimStart('\uFEFF'));
        if (grids != 1 || wrappers != 1 || !gridClosed || !wrapperClosed || Challenge(html)
            || html.QuerySelector(ListingGrid)?.Closest(CatalogForm) is not { } form
            || FindContinuation(html, destination) is not { } continuation)
            return false;

        IElement? pagination = continuation.Anchor.ParentElement;
        return continuation.Anchor.GetAttribute("data-testid") == "pagination-forward"
            && pagination?.Matches(PaginationList) == true
            && pagination.ParentElement?.Matches(PaginationWrapper) == true
            && pagination.Closest(CatalogForm) == form
            && Discover(html, destination) is Result<CaptureReadResult>.Success { Value.Discovered.Count: > 1 };
    }
}
