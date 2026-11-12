using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

/// <summary>Ports ListingShelf's OLX extraction into pure Harvest reading and source-aware availability evidence.</summary>
public sealed partial class OlxCaptureReader : ICaptureReader, ILinkAvailabilityReader, IFetchDestinationPolicy
{
    private const string ParserVersion = "olx-html@1";

    /// <inheritdoc />
    public string Key => "olx-uz";

    /// <inheritdoc />
    public bool Allows(Uri destination)
    {
        return OlxUrls.IsPublic(destination);
    }

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document)
    {
        return OlxUrls.IsPublic(document.FinalUrl) && Html(document);
    }

    /// <inheritdoc />
    public bool CanCheck(FetchedDocument document)
    {
        return OlxUrls.AdKey(document.RequestUrl) is not null;
    }

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document)) return Invalid("Use public OLX Uzbekistan HTML.");
        IDocument html = await new HtmlParser().ParseDocumentAsync(Decode(document), cancellationToken);
        if (Challenge(html)) return Invalid("OLX returned an access challenge; no listing evidence was extracted.");
        string? key = OlxUrls.AdKey(document.FinalUrl);
        string? requestedKey = OlxUrls.AdKey(document.RequestUrl);
        if (requestedKey is not null && requestedKey != key) return Invalid("The requested ad redirected away from its identity.");
        if (key is null) return Discover(html, document.FinalUrl);
        JsonElement? native = OlxEmbeddedData.Ad(html);
        bool matchedNative = NativeIdentity(native, key);
        if (Removed(html) || matchedNative && Inactive(native)) return Invalid("OLX says the ad is unavailable.");
        foreach (IElement related in html.QuerySelectorAll("[data-testid='listing-grid'], [data-testid='ad-card'], article.property-card")) related.Remove();
        JsonElement? original = OlxEmbeddedData.Product(html, key);
        bool identified = original is { } product && Uri.TryCreate(OlxEmbeddedData.Text(product, "url"), UriKind.Absolute, out Uri? productUrl)
            && OlxUrls.AdKey(productUrl) == key;
        bool visible = html.QuerySelector("[data-testid='offer_title'], [data-testid='ad_description']") is not null;
        if (!identified && !matchedNative && !visible) return Invalid("The response did not identify a listing detail page.");
        string? title = First(html, "[data-testid='offer_title'] h4, [data-cy='ad_title'], h1")
            ?? OlxEmbeddedData.Text(original, "name") ?? (matchedNative ? OlxEmbeddedData.Text(native, "title") : null);
        if (string.IsNullOrWhiteSpace(title)) return Invalid("The ad has no identifiable title.");
        IElement? description = html.QuerySelector("[data-testid='ad_description'] > div:last-child");
        string? bodyHtml = description?.InnerHtml ?? OlxEmbeddedData.Text(original, "description")
            ?? (matchedNative ? OlxEmbeddedData.Text(native, "description") : null);
        IElement? body = bodyHtml is null ? null : new HtmlParser().ParseDocument(bodyHtml).Body;
        string text = body is null ? string.Empty : Lines(body);
        string? priceText = First(html, "[data-testid='ad-price-container'] h3, [data-testid='ad-price-container']");
        string? seller = First(html, "[data-testid='user-profile-user-name']");
        string? postedRaw = First(html, "[data-testid='ad-posted-at'], time[datetime]");
        DateTimeOffset? published = Timestamp(matchedNative ? OlxEmbeddedData.Text(native, "createdTime") : OlxEmbeddedData.Text(original, "datePosted"));
        Dictionary<string, string> attributes = Attributes(html);
        CaptureMedia[] media = Images(html, original, document.FinalUrl);
        CaptureLink[] links = body?.QuerySelectorAll("a[href]").Select(anchor => new { anchor, url = Web(document.FinalUrl, anchor.GetAttribute("href")) })
            .Where(item => item.url is not null).Select(item => new CaptureLink { Url = item.url!, Text = Clean(item.anchor.TextContent) })
            .DistinctBy(link => link.Url).ToArray() ?? [];
        string[] contacts = body?.QuerySelectorAll("a[href]").Select(anchor => anchor.GetAttribute("href")!)
            .Where(href => href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Select(href => Uri.UnescapeDataString(href[(href.IndexOf(':') + 1)..].Split('?')[0])).ToArray() ?? [];
        Uri? declared = OlxUrls.Resolve(document.FinalUrl, html.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        string canonical = OlxUrls.DetailUrl(declared is not null && OlxUrls.AdKey(declared) == key ? declared : document.FinalUrl);
        JsonElement normalized = Product(original, title, text, canonical, seller, priceText, matchedNative ? native : null, media);
        string locale = html.DocumentElement?.GetAttribute("lang") ?? (document.FinalUrl.AbsolutePath.Contains("/oz/", StringComparison.Ordinal) ? "uz" : "ru");
        var fields = new Dictionary<string, object?>
        {
            ["externalId"] = key, ["title"] = title, ["descriptionHtml"] = bodyHtml, ["priceText"] = priceText,
            ["postedAtText"] = postedRaw, ["sellerName"] = seller, ["attributes"] = attributes, ["originalProduct"] = original,
        };
        if (matchedNative && native is { } ad)
            foreach (string name in new[] { "id", "category", "map", "isBusiness", "url", "createdTime", "lastRefreshTime", "validToTime", "isActive", "status", "params", "itemCondition", "price", "salary", "isJob", "photos", "location" })
                if (ad.TryGetProperty(name, out JsonElement value)) fields[name] = value.Clone();
        JsonElement metadata = JsonSerializer.SerializeToElement(new
        {
            reader = "page", source = Key, title, text, author = seller, contacts,
            sourceFields = new { schema = "olx.ad.v1", parserVersion = ParserVersion, fields },
        });
        var warnings = new List<string>();
        if (text.Length == 0) warnings.Add("description_not_published");
        if (!normalized.TryGetProperty("offers", out JsonElement normalizedOffer)
            || !decimal.TryParse(OlxEmbeddedData.Text(normalizedOffer, "price"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal statedPrice)
            || statedPrice < 0) warnings.Add("price_not_extracted");
        if (media.Length == 0) warnings.Add("photos_not_extracted");
        if (published is null) warnings.Add("publication_time_unresolved");
        var capture = new Capture
        {
            ExternalKey = key, Url = canonical, OriginKey = "olx.uz:ad:" + key,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode, ContentType = document.ContentType, CapturedAtUtc = document.FetchedAtUtc,
            Body = document.Body, Locale = locale, PublishedAtUtc = published,
            Structured = [normalized, metadata], Media = media, Links = links, Warnings = warnings,
            ContentProjection = JsonSerializer.Serialize(new { parserVersion = ParserVersion, key, normalized, attributes, published, links }),
        };
        return Result<CaptureReadResult>.Ok(new() { Captures = [capture], Complete = false });
    }

    /// <inheritdoc />
    public async Task<LinkObservation?> ReadAvailabilityAsync(FetchedDocument document, string externalKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanCheck(document) || OlxUrls.AdKey(document.RequestUrl) != externalKey
            || OlxUrls.AdKey(document.FinalUrl) != externalKey) return null;
        if (document.StatusCode is 404 or 410)
            return new() { ExternalKey = externalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_" + document.StatusCode, HttpStatus = document.StatusCode };
        if (!Html(document) || !OlxUrls.IsHost(document.FinalUrl.IdnHost) || document.StatusCode != 200) return null;
        IDocument html = await new HtmlParser().ParseDocumentAsync(Decode(document), cancellationToken);
        if (Challenge(html)) return null;
        JsonElement? native = OlxEmbeddedData.Ad(html);
        bool matched = NativeIdentity(native, externalKey);
        bool removed = Removed(html) || matched && Inactive(native);
        if (removed)
        {
            if (matched && native!.Value.TryGetProperty("isActive", out JsonElement active) && active.ValueKind == JsonValueKind.True) return null;
            return new() { ExternalKey = externalKey, Signal = LinkSignal.StrongNegative, Reason = "soft_404", HttpStatus = 200 };
        }
        if (OlxUrls.AdKey(document.FinalUrl) != externalKey) return null;
        Result<CaptureReadResult> read = await ReadAsync(document, cancellationToken);
        return read is Result<CaptureReadResult>.Success { Value.Captures.Count: 1 }
            ? new() { ExternalKey = externalKey, Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = 200 } : null;
    }

    private static Result<CaptureReadResult> Discover(IDocument html, Uri basis)
    {
        IElement? grid = html.QuerySelector("[data-testid='listing-grid']");
        bool empty = html.QuerySelector("[data-testid='no-results'], [data-testid='no-search-results'], [data-testid='empty-state']") is not null;
        if (empty) return Result<CaptureReadResult>.Ok(new() { Complete = false });
        if (grid is null && !empty) return Invalid("The catalog markup was not recognized; the search is not confirmed empty.");
        var ads = new Dictionary<string, DiscoveredUrl>(StringComparer.Ordinal);
        foreach (IElement anchor in grid?.QuerySelectorAll("a[href]").AsEnumerable() ?? Enumerable.Empty<IElement>())
        {
            Uri? url = OlxUrls.Resolve(basis, anchor.GetAttribute("href"));
            if (url is not null && OlxUrls.AdKey(url) is { } id) ads.TryAdd(id, new() { Url = OlxUrls.DetailUrl(url) });
        }
        if (ads.Count == 0 && !empty) return Invalid("The catalog exposed no recognizable ads or explicit empty state.");
        var targets = ads.Values.ToList();
        if (!empty)
            foreach (IElement anchor in html.QuerySelectorAll("a[data-testid='pagination-forward'], a[rel='next']"))
            {
                if (anchor.HasAttribute("disabled") || anchor.GetAttribute("aria-disabled") == "true" || anchor.ParentElement?.ClassList.Contains("disabled") == true) continue;
                if (OlxUrls.NextPage(basis, anchor.GetAttribute("href")) is { } next) { targets.Add(new() { Url = next.AbsoluteUri, IsListing = true }); break; }
            }
        return Result<CaptureReadResult>.Ok(new() { Discovered = targets, Complete = false });
    }

    private static JsonElement Product(
        JsonElement? original,
        string title,
        string text,
        string url,
        string? seller,
        string? priceText,
        JsonElement? native,
        CaptureMedia[] media)
    {
        var product = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = title, ["description"] = text,
            ["url"] = url, ["image"] = media.Select(item => item.Url).ToArray(),
        };
        var fields = new Dictionary<string, object?>();
        if (original is { } node && node.TryGetProperty("offers", out JsonElement offer) && offer.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty property in offer.EnumerateObject()) fields[property.Name] = property.Value.Clone();
        if (!fields.ContainsKey("price") && priceText is not null)
        {
            Match match = VisiblePrice().Match(priceText);
            if (match.Success)
            {
                fields["price"] = match.Groups["amount"].Value.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace("\u00a0", string.Empty, StringComparison.Ordinal).Replace(',', '.');
                string unit = match.Groups["currency"].Value.ToLowerInvariant();
                if (unit is "usd" or "$") fields["priceCurrency"] = "USD";
                else if (unit is "eur" or "€") fields["priceCurrency"] = "EUR";
                else if (unit.Length > 0) fields["priceCurrency"] = "UZS";
            }
        }
        bool explicitlyNotFree = native is { } known && known.TryGetProperty("price", out JsonElement nativePrice)
            && nativePrice.ValueKind == JsonValueKind.Object && nativePrice.TryGetProperty("free", out JsonElement free)
            && free.ValueKind == JsonValueKind.False;
        if (fields.TryGetValue("price", out object? amount)
            && decimal.TryParse(amount?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal numeric)
            && numeric == 0 && (explicitlyNotFree || Negotiation().IsMatch(priceText ?? string.Empty)))
            fields.Remove("price");
        if (seller is not null) fields["seller"] = new { name = seller };
        if (fields.Count > 0) product["offers"] = fields;
        if (native is { } ad && ad.TryGetProperty("location", out JsonElement location) && location.ValueKind == JsonValueKind.Object)
            product["location"] = new
            {
                name = OlxEmbeddedData.Text(location, "pathName"),
                address = new { addressLocality = OlxEmbeddedData.Text(location, "cityName"), addressRegion = OlxEmbeddedData.Text(location, "regionName") },
            };
        return JsonSerializer.SerializeToElement(product);
    }

    private static bool NativeIdentity(JsonElement? native, string key)
    {
        return Uri.TryCreate(OlxEmbeddedData.Text(native, "url"), UriKind.Absolute, out Uri? url) && OlxUrls.AdKey(url) == key;
    }

    private static bool Inactive(JsonElement? native)
    {
        return native is { } node && (node.TryGetProperty("isActive", out JsonElement active) && active.ValueKind == JsonValueKind.False
            || OlxEmbeddedData.Text(node, "status") is "inactive" or "removed" or "deleted" or "expired");
    }

    private static bool Removed(IDocument html)
    {
        return html.QuerySelector("[data-testid='ad-inactive'], [data-testid='ad-unavailable'], [data-testid='ad-not-found'], [data-cy='ad-inactive']") is not null
            || html.QuerySelectorAll("h1, h2, [data-testid='ad-status']").Any(node => MissingText().IsMatch(Clean(node.TextContent)));
    }

    private static bool Challenge(IDocument html)
    {
        return ChallengeTitle().IsMatch(html.Title ?? string.Empty) || html.QuerySelector("#challenge-form, #cf-challenge-running, form[action*='captcha']") is not null;
    }

    private static Dictionary<string, string> Attributes(IDocument html)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (IElement node in html.QuerySelectorAll("[data-testid='ad-parameters-container'] p, [itemprop='additionalProperty']"))
        {
            string text = Clean(node.TextContent);
            if (text.Length == 0) continue;
            string[] parts = text.Split(':', 2, StringSplitOptions.TrimEntries);
            fields[parts[0]] = parts.Length == 2 ? parts[1] : text;
        }
        return fields;
    }

    private static CaptureMedia[] Images(IDocument html, JsonElement? original, Uri basis)
    {
        var candidates = html.QuerySelectorAll("[data-testid='swiper-image'], .swiper img, meta[property='og:image']")
            .Select(node => node.GetAttribute("content") ?? node.GetAttribute("data-src") ?? node.GetAttribute("src")).ToList();
        if (original is { } node && node.TryGetProperty("image", out JsonElement image))
            candidates.AddRange((image.ValueKind == JsonValueKind.Array ? image.EnumerateArray().ToArray() : [image])
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : OlxEmbeddedData.Text(value, "url")));
        var urls = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (string? raw in candidates)
        {
            if (!Uri.TryCreate(basis, raw, out Uri? uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0
                || !(uri.IdnHost == "olxcdn.com" || uri.IdnHost.EndsWith(".olxcdn.com", StringComparison.OrdinalIgnoreCase))) continue;
            string key = uri.GetLeftPart(UriPartial.Path).Split(";s=", 2, StringSplitOptions.None)[0];
            if (!urls.TryGetValue(key, out Uri? current) || current.AbsoluteUri.Contains(";s=", StringComparison.Ordinal) && !uri.AbsoluteUri.Contains(";s=", StringComparison.Ordinal)) urls[key] = uri;
        }
        return urls.Values.Select((uri, index) => new CaptureMedia { Url = uri.AbsoluteUri, Kind = MediaKind.Image, Position = index }).ToArray();
    }

    private static string? First(IDocument html, string selectors)
    {
        foreach (string selector in selectors.Split(','))
        {
            IElement? element = html.QuerySelector(selector.Trim());
            string value = Clean(element?.GetAttribute("datetime") ?? element?.TextContent);
            if (value.Length > 0) return value;
        }
        return null;
    }

    private static string Lines(INode node)
    {
        return string.Join('\n', string.Join(' ', Parts(node)).Split('\n').Select(Clean).Where(line => line.Length > 0));
    }

    private static IEnumerable<string> Parts(INode node)
    {
        if (node is IText text) yield return text.Data;
        else if (node is not IElement element || element.LocalName is not ("script" or "style" or "nav" or "aside" or "footer"))
        {
            bool block = node is IElement blockNode && blockNode.LocalName is "p" or "li" or "br" or "div";
            if (block) yield return "\n";
            foreach (INode child in node.ChildNodes)
                foreach (string part in Parts(child)) yield return part;
            if (block) yield return "\n";
        }
    }

    private static DateTimeOffset? Timestamp(string? text)
    {
        return text is not null && OffsetTimestamp().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value) ? value.ToUniversalTime() : null;
    }

    private static bool Html(FetchedDocument document)
    {
        return document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string Decode(FetchedDocument document)
    {
        Encoding encoding = Encoding.UTF8;
        if (document.Charset is { Length: > 0 } charset)
            try { encoding = Encoding.GetEncoding(charset.Trim('\'', '"')); } catch (ArgumentException) { }
        return encoding.GetString(document.Body.Span).TrimStart('\uFEFF');
    }

    private static string Clean(string? text)
    {
        return Space().Replace(text ?? string.Empty, " ").Trim().Normalize(NormalizationForm.FormC);
    }

    private static string? Web(Uri basis, string? raw)
    {
        return Uri.TryCreate(basis, raw, out Uri? uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 ? uri.AbsoluteUri : null;
    }

    private static Result<CaptureReadResult> Invalid(string message)
    {
        return Result<CaptureReadResult>.Fail(AppErrorFactory.SerializationFailed(message));
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Space();
    [GeneratedRegex(@"^(?:объявление (?:неактивно|больше недоступно|больше не доступно)|это объявление больше недоступно|this ad is (?:inactive|no longer available)|e['’ʻ]lon (?:faol emas|mavjud emas))\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MissingText();
    [GeneratedRegex(@"access denied|just a moment|captcha|attention required", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChallengeTitle();
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex OffsetTimestamp();
    [GeneratedRegex(@"^\s*(?<amount>\d+(?:[ \u00a0]\d{3})*(?:[.,]\d{1,2})?)\s*(?<currency>USD|UZS|EUR|\$|€|so['‘’ʻ`]?m|сўм|сум)?(?:\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VisiblePrice();
    [GeneratedRegex(@"договорн|келиш|kelish|negotiab|обмен|ayirboshlash", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Negotiation();
}
