using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Domtut;

/// <summary>Ports ListingShelf's Domtut extraction into pure Harvest reading and source-aware availability evidence.</summary>
/// <remarks>A Domtut listing is a residential-complex page under <c>/nedvizhimost/{slug}</c>. Its key is the numeric post
/// ID the page carries, not the slug, so a renamed slug keeps the post.</remarks>
public sealed partial class DomtutCaptureReader : ICaptureReader, ILinkAvailabilityReader, IFetchDestinationPolicy
{
    private const string ParserVersion = "domtut-html@1";

    /// <inheritdoc />
    public string Key => "domtut-uz";

    /// <inheritdoc />
    public bool Allows(Uri destination)
    {
        return DomtutUrls.IsPublic(destination);
    }

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document)
    {
        return DomtutUrls.IsPublic(document.FinalUrl) && Html(document);
    }

    /// <inheritdoc />
    public bool CanCheck(FetchedDocument document)
    {
        return DomtutUrls.Slug(document.RequestUrl) is not null;
    }

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document)) return Invalid("Use public Domtut HTML.");
        IDocument html = await new HtmlParser().ParseDocumentAsync(Decode(document), cancellationToken);
        if (Challenge(html)) return Invalid("Domtut returned an access challenge; no listing evidence was extracted.");
        string? slug = DomtutUrls.Slug(document.FinalUrl);
        if (DomtutUrls.Slug(document.RequestUrl) is not null && slug is null) return Invalid("The requested listing redirected away from its detail page.");
        if (slug is null) return Discover(html, document.FinalUrl);
        IElement? property = Property(html);
        if (Removed(html, property)) return Invalid("Domtut says the listing is unavailable.");
        string? key = Id(property);
        if (key is null) return Invalid("The response did not identify a listing detail page.");
        Expand(html);
        foreach (IElement related in html.QuerySelectorAll("article.property-card, #location .markers")) related.Remove();
        // Domtut renders some amounts client-side from a uzs attribute; the attribute is the published figure.
        foreach (IElement price in html.QuerySelectorAll(".price[uzs]").Where(node => Clean(node.TextContent).Length == 0))
            price.TextContent = price.GetAttribute("uzs") ?? string.Empty;
        JsonElement? original = Original(html, slug);
        JsonElement? originalOffer = original is { } product && product.TryGetProperty("offers", out JsonElement offers)
            ? offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().FirstOrDefault() : offers : null;
        string? title = First(html, ".object[itemprop='name'], h1") ?? Text(original, "name");
        if (string.IsNullOrWhiteSpace(title)) return Invalid("The listing has no identifiable title.");
        IElement? description = html.QuerySelector("#description .description .content");
        string? bodyHtml = description is not null && Clean(description.TextContent).Length > 0 ? description.InnerHtml : Text(original, "description");
        IElement? body = bodyHtml is null ? null : new HtmlParser().ParseDocument(bodyHtml).Body;
        string text = body is null ? string.Empty : Lines(body);

        IElement? block = html.QuerySelector(".price-block");
        IElement? main = block?.QuerySelector(".main-price");
        bool negotiable = block?.ClassList.Contains("price-negotiate") == true;
        string? priceText = negotiable ? NonEmpty(block!.QuerySelector(".label-negotiate")) : NonEmpty(main);
        string? amount = negotiable ? null : Amount(main?.QuerySelector(".price")?.TextContent)
            ?? Amount(block?.QuerySelector("meta[itemprop='lowPrice']")?.GetAttribute("content"))
            ?? Amount(block?.QuerySelector("meta[itemprop='price']")?.GetAttribute("content"))
            ?? Amount(Text(originalOffer, "lowPrice")) ?? Amount(Text(originalOffer, "price"));
        string? currency = Currency(NonEmpty(main?.QuerySelector(".price_postfix")))
            ?? Code(block?.QuerySelector("meta[itemprop='priceCurrency']")?.GetAttribute("content")) ?? Code(Text(originalOffer, "priceCurrency"));
        bool from = main?.QuerySelector(".price-from") is not null || priceText?.StartsWith("от ", StringComparison.OrdinalIgnoreCase) == true;
        string? perMeter = block?.Children.Where(child => child.LocalName == "span").Select(child => Clean(child.TextContent))
            .FirstOrDefault(value => value.Contains("м²", StringComparison.Ordinal) || value.Contains("m²", StringComparison.Ordinal));
        string? availability = block?.QuerySelector("meta[itemprop='availability']")?.GetAttribute("content") ?? Text(originalOffer, "availability");
        string? updated = First(html, ".updated");
        string? seller = First(html, "nav .author a");
        string? authorText = First(html, "nav .author");

        IElement? address = html.QuerySelector(".slideshow-text address");
        string? addressLine = NonEmpty(address);
        string? city = null, district = null, districtKey = null;
        foreach (IElement anchor in address?.QuerySelectorAll("a[href]").AsEnumerable() ?? Enumerable.Empty<IElement>())
        {
            string href = anchor.GetAttribute("href")!;
            Match area = DistrictParameter().Match(href);
            if (area.Success) { district ??= NonEmpty(anchor); districtKey ??= Uri.UnescapeDataString(area.Groups["value"].Value); }
            else if (href.Contains("city=", StringComparison.Ordinal)) city ??= NonEmpty(anchor);
        }
        string? street = address is null ? null : Clean(string.Concat(address.ChildNodes.OfType<IText>().Select(node => node.Data))).Trim(',', ' ');
        if (string.IsNullOrEmpty(street))
            street = original is { } node && node.TryGetProperty("address", out JsonElement postal) ? Text(postal, "streetAddress") : null;
        if (string.IsNullOrEmpty(street)) street = null;
        Dictionary<string, string>? geo = Geo(html.GetElementById("map")?.GetAttribute("v-bind"));

        Dictionary<string, string> attributes = Attributes(html);
        var layouts = html.QuerySelectorAll("#layouts .floor-plans .plan-row").Select(row =>
        {
            IElement? head = row.QuerySelector(".plan_title");
            string? name = head is null ? null : Clean(string.Concat(head.ChildNodes.OfType<IText>().Select(node => node.Data)));
            return new
            {
                title = string.IsNullOrEmpty(name) ? null : name, rooms = name is not null && Rooms().Match(name) is { Success: true } count ? count.Value : null,
                area = NonEmpty(head?.QuerySelector(".meter")), price = row.QuerySelector(".price-total .price")?.GetAttribute("uzs"),
                pricePerMeter = row.QuerySelector(".price-meter .price")?.GetAttribute("uzs"), offers = NonEmpty(row.QuerySelector(".plan-head > a")),
            };
        }).Where(layout => layout.title is not null).ToArray();
        string[] labels = html.QuerySelectorAll("main.gallery-card .line-offers a").Select(anchor => Clean(anchor.TextContent))
            .Where(label => label.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        CaptureMedia[] media = Images(html, original, document.FinalUrl);
        CaptureLink[] links = body?.QuerySelectorAll("a[href]").Select(anchor => new { anchor, url = Web(document.FinalUrl, anchor.GetAttribute("href")) })
            .Where(item => item.url is not null).Select(item => new CaptureLink { Url = item.url!, Text = Clean(item.anchor.TextContent) })
            .DistinctBy(link => link.Url).ToArray() ?? [];
        string[] contacts = body?.QuerySelectorAll("a[href]").Select(anchor => anchor.GetAttribute("href")!)
            .Where(href => href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Select(href => Uri.UnescapeDataString(href[(href.IndexOf(':') + 1)..].Split('?')[0])).ToArray() ?? [];
        Uri? declared = DomtutUrls.Resolve(document.FinalUrl, html.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        string canonical = DomtutUrls.DetailUrl(declared is not null && DomtutUrls.Slug(declared) == slug ? declared : document.FinalUrl);

        var offer = new Dictionary<string, object?>();
        if (amount is not null)
        {
            offer["@type"] = from ? "AggregateOffer" : "Offer";
            offer[from ? "lowPrice" : "price"] = amount;
            if (currency is not null) offer["priceCurrency"] = currency;
        }
        if (availability is not null) offer["availability"] = availability;
        if (seller is not null) offer["seller"] = new { name = seller };
        var postalAddress = new Dictionary<string, object?>();
        if (city is not null) postalAddress["addressLocality"] = city;
        if (district is not null) postalAddress["addressRegion"] = district;
        if (street is not null) postalAddress["streetAddress"] = street;
        var location = new Dictionary<string, object?>();
        if (addressLine is not null) location["name"] = addressLine;
        if (postalAddress.Count > 0) location["address"] = postalAddress;
        if (geo is not null) location["geo"] = geo;
        var normalizedProduct = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Product", ["name"] = title, ["description"] = text,
            ["url"] = canonical, ["image"] = media.Select(item => item.Url).ToArray(),
        };
        if (offer.Count > 0) normalizedProduct["offers"] = offer;
        if (location.Count > 0) normalizedProduct["location"] = location;
        JsonElement normalized = JsonSerializer.SerializeToElement(normalizedProduct);

        string? status = property!.ClassList.FirstOrDefault(name => name.StartsWith("status-", StringComparison.Ordinal))?["status-".Length..];
        string locale = html.DocumentElement?.GetAttribute("lang") ?? (document.FinalUrl.AbsolutePath.StartsWith("/uz/", StringComparison.Ordinal) ? "uz" : "ru");
        var fields = new Dictionary<string, object?>
        {
            ["externalId"] = key, ["slug"] = slug, ["title"] = title, ["descriptionHtml"] = bodyHtml, ["priceText"] = priceText,
            ["pricePerMeterText"] = perMeter, ["priceNegotiable"] = negotiable, ["currency"] = currency, ["updatedText"] = updated,
            ["sellerName"] = seller, ["authorText"] = authorText, ["address"] = addressLine, ["city"] = city, ["district"] = district,
            ["districtKey"] = districtKey, ["street"] = street, ["coordinates"] = geo, ["availability"] = availability, ["status"] = status,
            ["labels"] = labels, ["attributes"] = attributes, ["layouts"] = layouts, ["originalProduct"] = original,
        };
        JsonElement metadata = JsonSerializer.SerializeToElement(new
        {
            reader = "page", source = Key, title, text, author = seller, contacts,
            sourceFields = new { schema = "domtut.ad.v1", parserVersion = ParserVersion, fields },
        });
        var warnings = new List<string>();
        if (text.Length == 0) warnings.Add("description_not_published");
        if (amount is null) warnings.Add("price_not_extracted");
        if (media.Length == 0) warnings.Add("photos_not_extracted");
        warnings.Add("publication_time_unresolved");
        var capture = new Capture
        {
            ExternalKey = key, Url = canonical, OriginKey = "domtut.uz:ad:" + key,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode, ContentType = document.ContentType, CapturedAtUtc = document.FetchedAtUtc,
            Body = document.Body, Locale = locale, PublishedAtUtc = null,
            Structured = [normalized, metadata], Media = media, Links = links, Warnings = warnings,
            // The "updated" stamp and offer validity move daily without a content change; they stay out of the hash.
            ContentProjection = JsonSerializer.Serialize(new { parserVersion = ParserVersion, key, normalized, attributes, layouts, labels, links }),
        };
        return Result<CaptureReadResult>.Ok(new() { Captures = [capture], Complete = false });
    }

    /// <inheritdoc />
    public async Task<LinkObservation?> ReadAvailabilityAsync(FetchedDocument document, string externalKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanCheck(document) || !NumericId().IsMatch(externalKey)) return null;
        string? requested = DomtutUrls.Slug(document.RequestUrl);
        string? landed = DomtutUrls.Slug(document.FinalUrl);
        if (document.StatusCode is 404 or 410)
            return landed == requested
                ? new() { ExternalKey = externalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_" + document.StatusCode, HttpStatus = document.StatusCode }
                : null;
        if (landed is null || !Html(document) || document.StatusCode != 200) return null;
        IDocument html = await new HtmlParser().ParseDocumentAsync(Decode(document), cancellationToken);
        if (Challenge(html)) return null;
        IElement? property = Property(html);
        string? id = Id(property);
        if (id is not null && id != externalKey) return null;
        if (Removed(html, property))
            return id == externalKey || id is null && landed == requested
                ? new() { ExternalKey = externalKey, Signal = LinkSignal.StrongNegative, Reason = "soft_404", HttpStatus = 200 }
                : null;
        if (id is null) return null;
        Result<CaptureReadResult> read = await ReadAsync(document, cancellationToken);
        return read is Result<CaptureReadResult>.Success { Value.Captures: [{ } capture] } && capture.ExternalKey == externalKey
            ? new() { ExternalKey = externalKey, Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = 200 } : null;
    }

    private static Result<CaptureReadResult> Discover(IDocument html, Uri basis)
    {
        bool empty = html.QuerySelector(".property-list .no-results, .property-list .empty-results, .catalog-list .no-results, .catalog-list .empty-results") is not null;
        if (empty) return Result<CaptureReadResult>.Ok(new() { Complete = false });
        IElement? list = html.QuerySelector(".property-list");
        if (list is null) return Invalid("The catalog markup was not recognized; the search is not confirmed empty.");
        var listings = new Dictionary<string, DiscoveredUrl>(StringComparer.Ordinal);
        foreach (IElement anchor in list.QuerySelectorAll("article.property-card link[itemprop='url'][href], article.property-card a[href]"))
        {
            Uri? url = DomtutUrls.Resolve(basis, anchor.GetAttribute("href"));
            if (url is not null && DomtutUrls.Slug(url) is { } slug) listings.TryAdd(slug, new() { Url = DomtutUrls.DetailUrl(url) });
        }
        if (listings.Count == 0) return Invalid("The catalog exposed no recognizable listings or explicit empty state.");
        var targets = listings.Values.ToList();
        foreach (IElement anchor in html.QuerySelectorAll(".pagination .page-next a, a[rel='next']"))
        {
            if (anchor.HasAttribute("disabled") || anchor.GetAttribute("aria-disabled") == "true" || anchor.ParentElement?.ClassList.Contains("disabled") == true) continue;
            if (DomtutUrls.NextPage(basis, anchor.GetAttribute("href")) is { } next) { targets.Add(new() { Url = next.AbsoluteUri, IsListing = true }); break; }
        }
        return Result<CaptureReadResult>.Ok(new() { Discovered = targets, Complete = false });
    }

    private static IElement? Property(IDocument html)
    {
        return html.QuerySelector(".property[data-id]");
    }

    private static string? Id(IElement? property)
    {
        string? id = property?.GetAttribute("data-id")?.Trim();
        return id is not null && NumericId().IsMatch(id) ? id : null;
    }

    private static bool Removed(IDocument html, IElement? property)
    {
        if (property is not null) return property.ClassList.Any(name => RemovedStatus().IsMatch(name));
        return NotFoundTitle().IsMatch(Clean(html.Title))
            || html.QuerySelectorAll("h1, h2").Any(node => NotFoundHeading().IsMatch(Clean(node.TextContent)));
    }

    private static bool Challenge(IDocument html)
    {
        return ChallengeTitle().IsMatch(html.Title ?? string.Empty) || html.QuerySelector("#challenge-form, #cf-challenge-running, form[action*='captcha']") is not null;
    }

    /// <summary>Moves Vue <c>template</c> contents into the document: Domtut hides most listing specs inside them.</summary>
    private static void Expand(IDocument html)
    {
        while (html.QuerySelector("template") is IHtmlTemplateElement template)
            template.Replace(template.Content.ChildNodes.ToArray());
    }

    private static Dictionary<string, string> Attributes(IDocument html)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? name, string? value)
        {
            string key = Clean(name).TrimEnd(':').Trim();
            string text = Clean(value);
            if (key.Length > 0 && text.Length > 0) fields.TryAdd(key, text);
        }
        foreach (IElement stat in html.QuerySelectorAll("#important .list-imp .stat")) Add(stat.QuerySelector(".gray")?.TextContent, stat.QuerySelector("b")?.TextContent);
        foreach (IElement item in html.QuerySelectorAll("#specs .spec-list .item")) Add(item.QuerySelector(".name")?.TextContent, item.QuerySelector(".val")?.TextContent);
        foreach (IElement item in html.QuerySelectorAll("#location .map-info .item")) Add(item.QuerySelector("b")?.TextContent, item.QuerySelector("span")?.TextContent);
        return fields;
    }

    private static CaptureMedia[] Images(IDocument html, JsonElement? original, Uri basis)
    {
        var candidates = html.QuerySelectorAll("[data-fancybox='main-gallery']").Select(node => node.GetAttribute("data-src")).ToList();
        candidates.AddRange(html.QuerySelectorAll("main.gallery-card img[itemprop='image']").Select(node => node.GetAttribute("content") ?? node.GetAttribute("data-lazy-src")));
        candidates.AddRange(html.QuerySelectorAll("meta[property='og:image']").Select(node => node.GetAttribute("content")));
        if (original is { } node && node.TryGetProperty("image", out JsonElement image))
            candidates.AddRange((image.ValueKind == JsonValueKind.Array ? image.EnumerateArray().ToArray() : [image])
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : Text(value, "url")));
        var urls = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (string? raw in candidates)
        {
            if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(basis, raw, out Uri? uri) || uri.Scheme != "https" || !uri.IsDefaultPort
                || uri.UserInfo.Length > 0 || !DomtutUrls.IsHost(uri.IdnHost) || !Photo().IsMatch(uri.AbsolutePath)) continue;
            // A thumbnail and its full photo share the file name under thumbs/ and property/, sometimes in another format.
            string key = ImageExtension().Replace(uri.AbsolutePath.Replace("/thumbs/", "/property/", StringComparison.Ordinal), string.Empty);
            bool thumbnail = uri.AbsolutePath.Contains("/thumbs/", StringComparison.Ordinal);
            if (!urls.TryGetValue(key, out Uri? current) || current.AbsolutePath.Contains("/thumbs/", StringComparison.Ordinal) && !thumbnail)
                urls[key] = new Uri(uri.GetLeftPart(UriPartial.Path));
        }
        return urls.Values.Select((uri, index) => new CaptureMedia { Url = uri.AbsoluteUri, Kind = MediaKind.Image, Position = index }).ToArray();
    }

    private static JsonElement? Original(IDocument html, string slug)
    {
        foreach (IElement script in html.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using JsonDocument json = JsonDocument.Parse(script.TextContent, new() { MaxDepth = 64 });
                foreach (JsonElement node in Nodes(json.RootElement))
                {
                    if (!node.TryGetProperty("@type", out JsonElement type) || !(type.ValueKind == JsonValueKind.String && type.GetString() == "Product"
                        || type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "Product")))
                        continue;
                    string? url = Text(node, "url");
                    if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out Uri? identity) || DomtutUrls.Slug(identity) != slug)) continue;
                    return node.Clone();
                }
            }
            catch (JsonException) { /* Visible listing fields survive malformed optional metadata. */ }
        }
        return null;
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

    private static string? Text(JsonElement? node, string property)
    {
        if (node is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty(property, out JsonElement item)) return null;
        return item.ValueKind is JsonValueKind.String ? item.GetString()
            : item.ValueKind is JsonValueKind.Number ? item.GetRawText() : null;
    }

    private static Dictionary<string, string>? Geo(string? binding)
    {
        Match match = MapPoint().Match(binding ?? string.Empty);
        if (!match.Success
            || !double.TryParse(match.Groups["lat"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) || latitude is < -90 or > 90
            || !double.TryParse(match.Groups["lng"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) || longitude is < -180 or > 180)
            return null;
        return new() { ["latitude"] = match.Groups["lat"].Value, ["longitude"] = match.Groups["lng"].Value };
    }

    private static string? Amount(string? raw)
    {
        string digits = Space().Replace(raw ?? string.Empty, string.Empty).Replace(',', '.');
        return PlainAmount().IsMatch(digits) && decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && value > 0
            ? digits : null;
    }

    private static string? Currency(string? unit)
    {
        return unit?.ToLowerInvariant().Trim('.', ' ') switch
        {
            "сум" or "сўм" or "so'm" or "so‘m" or "so’m" or "soʻm" or "som" or "uzs" => "UZS",
            "$" or "usd" or "у.е" or "y.e" => "USD",
            "€" or "eur" => "EUR",
            _ => null,
        };
    }

    private static string? Code(string? value)
    {
        return value is not null && CurrencyCode().IsMatch(value.Trim()) ? value.Trim().ToUpperInvariant() : null;
    }

    private static string? NonEmpty(IElement? element)
    {
        string value = Clean(element?.TextContent);
        return value.Length > 0 ? value : null;
    }

    private static string? First(IDocument html, string selectors)
    {
        foreach (string selector in selectors.Split(','))
        {
            string value = Clean(html.QuerySelector(selector.Trim())?.TextContent);
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
            bool block = node is IElement blockNode && blockNode.LocalName is "p" or "li" or "br" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
            if (block) yield return "\n";
            foreach (INode child in node.ChildNodes)
                foreach (string part in Parts(child)) yield return part;
            if (block) yield return "\n";
        }
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
        return encoding.GetString(document.Body.Span).TrimStart('﻿');
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
    [GeneratedRegex(@"^\d{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericId();
    [GeneratedRegex(@"^status-(?:trash|deleted|draft|private|expired|archived?)$", RegexOptions.CultureInvariant)]
    private static partial Regex RemovedStatus();
    [GeneratedRegex(@"^(?:404\b|страница не найдена|объект не найден|sahifa topilmadi|page not found)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotFoundTitle();
    [GeneratedRegex(@"^(?:(?:ошибка )?404|страница не найдена|объект не найден|объявление (?:удалено|не найдено|снято с публикации)|sahifa topilmadi|e['’ʻ]lon (?:topilmadi|o['’ʻ]chirilgan)|page not found)[.!]?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NotFoundHeading();
    [GeneratedRegex(@"access denied|just a moment|captcha|attention required", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChallengeTitle();
    [GeneratedRegex(@"[?&]district=(?<value>[^&#]+)", RegexOptions.CultureInvariant)]
    private static partial Regex DistrictParameter();
    [GeneratedRegex(@"initMap\(\s*'(?<lat>-?\d{1,2}(?:\.\d+)?)\s*,\s*(?<lng>-?\d{1,3}(?:\.\d+)?)\s*'", RegexOptions.CultureInvariant)]
    private static partial Regex MapPoint();
    [GeneratedRegex(@"^\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Rooms();
    [GeneratedRegex(@"^\d+(?:\.\d{1,2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainAmount();
    [GeneratedRegex(@"^[A-Za-z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyCode();
    [GeneratedRegex(@"^/resources/uploads/(?:property|thumbs)/(?:[^/]+/)+[^/]+\.(?:jpe?g|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Photo();
    [GeneratedRegex(@"\.(?:jpe?g|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageExtension();
}
