using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;

/// <summary>Discovers apartment rentals from BirBir's catalog and reads the published listing details.</summary>
public sealed partial class BirBirCaptureReader : ICaptureReader, IFetchDestinationPolicy
{
    private const string ParserVersion = "birbir-html@1";
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");

    /// <inheritdoc />
    public string Key => "birbir-uz";

    /// <inheritdoc />
    public bool Allows(Uri destination) =>
        BirBirListingUrls.IsCatalog(destination) || BirBirListingUrls.ListingId(destination) is not null;

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => Allows(document.FinalUrl);

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        if (!CanRead(document) || !Allows(document.RequestUrl) || document.StatusCode != 200
            || document.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) != true)
            return Invalid("Expected an HTML response from a BirBir apartment rental page.");

        string? id = BirBirListingUrls.ListingId(document.FinalUrl);
        if (BirBirListingUrls.ListingId(document.RequestUrl) != id)
            return Invalid("The response changed the requested listing's identity.");

        var html = await new HtmlParser().ParseDocumentAsync(Encoding.UTF8.GetString(document.Body.Span), cancellationToken);
        if (html.QuerySelector("#challenge-form, #cf-challenge-running") is not null)
            return Invalid("BirBir returned an access challenge.");
        return id is null ? Discover(html, document.FinalUrl) : ReadListing(html, document, id);
    }

    private static Result<CaptureReadResult> Discover(IDocument html, Uri url)
    {
        var list = html.QuerySelector("main [data-testid='announcements_list']");
        if (list is null)
            return Invalid("The BirBir catalog did not expose its listing grid.");

        var found = new Dictionary<string, DiscoveredUrl>(StringComparer.Ordinal);
        foreach (var anchor in list.QuerySelectorAll("[data-testid^='feed_item-'] a[href]"))
        {
            if (Uri.TryCreate(url, anchor.GetAttribute("href"), out var target) && BirBirListingUrls.ListingId(target) is { } id)
                found.TryAdd(id, new() { Url = target.AbsoluteUri, ExternalKey = id });
        }

        return found.Count == 0 ? Invalid("The catalog contained no recognizable BirBir apartment rental links.")
            : Result<CaptureReadResult>.Ok(new() { Discovered = [.. found.Values], Complete = false });
    }

    private Result<CaptureReadResult> ReadListing(IDocument html, FetchedDocument document, string id)
    {
        var product = Product(html, document.FinalUrl, id);
        var offer = Property(product, "offers");
        var title = BirBirSourceFields.ReadText(ElementText(html, "offer-details_title") ?? Text(product, "name"), BirBirSourceFields.TitleLimit);
        if (product.ValueKind == JsonValueKind.Undefined || title is null
            || ElementText(html, "offer_number", "> :last-child") is { } printedId && printedId.Trim() != id)
            return Invalid("The page did not identify the requested BirBir apartment listing.");

        var attributes = ReadAttributes(product);
        var rooms = Feature(attributes, "Комнаты", "xonalar");
        var amount = PositiveDecimal(Text(offer, "price"));
        var currencyCode = Text(offer, "priceCurrency");
        var priceText = BirBirSourceFields.ReadText(ElementText(html, "offer-details_price"), BirBirSourceFields.PriceTextLimit);
        var place = Property(Property(offer, "availableAtOrFrom"), "address");
        var location = BirBirSourceFields.ReadText(ElementText(html, "offer_delivery_address", "p:last-child"), BirBirSourceFields.LabelLimit);
        var dateText = BirBirSourceFields.ReadText(ElementText(html, "offer_time", "> :last-child"), BirBirSourceFields.DateTextLimit);
        var published = DateTime.TryParseExact(dateText, "dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Tashkent)) : (DateTimeOffset?)null;
        var images = ReadImages(product);
        var currency = currencyCode switch
        {
            "UZS" => BirBirCurrency.Uzs,
            "USD" => BirBirCurrency.Usd,
            _ => (BirBirCurrency?)null,
        };

        var fields = new BirBirSourceFields
        {
            ExternalId = id,
            Url = document.FinalUrl.AbsoluteUri,
            Locale = BirBirSourceFields.LocaleOf(document.FinalUrl),
            Title = title,
            Description = BirBirSourceFields.ReadText(Text(product, "description")
                ?? ElementText(html, "offer_description", "> p"), BirBirSourceFields.DescriptionLimit),
            PriceText = priceText ?? (amount is { } value && currency is not null
                ? string.Create(CultureInfo.InvariantCulture, $"{value} {currencyCode}") : null),
            PriceAmount = amount,
            Currency = currency,
            Condition = Text(offer, "itemCondition") switch
            {
                "https://schema.org/NewCondition" => BirBirCondition.New,
                "https://schema.org/UsedCondition" => BirBirCondition.Used,
                _ => null,
            },
            Availability = Text(offer, "availability") switch
            {
                "https://schema.org/InStock" => BirBirAvailability.InStock,
                "https://schema.org/OutOfStock" => BirBirAvailability.OutOfStock,
                _ => null,
            },
            AreaSquareMetres = PositiveDecimal(Feature(attributes, "Площадь", "Kvadrat")),
            RoomsText = BirBirSourceFields.ReadText(rooms, BirBirSourceFields.LabelLimit),
            RoomCount = WholeNumber(RoomNumber().Match(rooms ?? string.Empty).Groups["rooms"].Value) is > 0 and var count ? count : null,
            Floor = WholeNumber(Feature(attributes, "Этаж", "Qavat")),
            BuildingFloors = WholeNumber(Feature(attributes, "Этажность здания", "Qavatlar soni")) is > 0 and var floors ? floors : null,
            City = BirBirSourceFields.ReadText(Text(place, "addressLocality")
                ?? Text(Property(offer, "areaServed"), "name"), BirBirSourceFields.LabelLimit),
            CountryCode = Text(place, "addressCountry") is { Length: 2 } country && country.All(char.IsAsciiLetterUpper) ? country : null,
            District = location is not null && DistrictLabel().IsMatch(location) ? location : null,
            LocationText = location,
            AddressText = BirBirSourceFields.ReadText(ElementText(html, "offer_delivery_address", "p:first-child"), BirBirSourceFields.LabelLimit),
            SellerName = BirBirSourceFields.ReadText(Text(Property(offer, "seller"), "name")
                ?? ElementText(html, "offer-owner_name"), BirBirSourceFields.LabelLimit),
            PostedAtText = dateText,
            PublishedAtUtc = published,
            OfferValidFrom = DateOnly.TryParseExact(Text(offer, "validFrom"), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var validFrom) ? validFrom : null,
            ImageUrls = images,
            Attributes = attributes,
        };
        var sourceFields = fields.ToEnvelope(ParserVersion);
        var listing = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["@type"] = "Product",
            ["name"] = fields.Title,
            ["description"] = fields.Description,
            ["offers"] = new
            {
                price = fields.PriceAmount, priceCurrency = fields.Currency is not null ? currencyCode : null,
                seller = fields.SellerName is { } name ? new { name } : null,
                areaServed = new { name = fields.LocationText ?? fields.City },
            },
        });
        var capture = new Capture
        {
            ExternalKey = id, Url = fields.Url, OriginKey = $"birbir.uz:ad:{id}",
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode, ContentType = document.ContentType, CapturedAtUtc = document.FetchedAtUtc,
            PublishedAtUtc = published, Body = document.Body,
            Locale = fields.Locale == BirBirLocale.Uz ? "uz" : "ru",
            ContentProjection = sourceFields.GetRawText(),
            Structured = [listing, JsonSerializer.SerializeToElement(new { reader = "page", source = Key, sourceFields })],
            Media = images.Select(url => new CaptureMedia { Kind = MediaKind.Image, Url = url }).ToArray(),
        };
        return Result<CaptureReadResult>.Ok(new() { Captures = [capture], Complete = false });
    }

    private static JsonElement Product(IDocument html, Uri basis, string id)
    {
        foreach (var tag in html.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(tag.TextContent);
                var product = json.RootElement;
                if (Text(product, "@type") == "Product" && Text(product, "sku") == id
                    && Uri.TryCreate(basis, Text(Property(product, "offers"), "url"), out var offer)
                    && BirBirListingUrls.ListingId(offer) == id)
                    return product.Clone();
            }
            catch (JsonException)
            {
                // Other site metadata may be malformed. Only a matching Product identifies the listing.
            }
        }
        return default;
    }

    private static SortedDictionary<string, string> ReadAttributes(JsonElement product)
    {
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var nodes = Property(product, "additionalProperty");
        if (nodes.ValueKind != JsonValueKind.Array)
            return attributes;

        foreach (var item in nodes.EnumerateArray())
        {
            var key = Text(item, "name")?.Trim();
            var value = BirBirSourceFields.ReadText(Text(item, "value"), BirBirSourceFields.AttributeValueLimit);
            if (key is { Length: > 0 and <= BirBirSourceFields.AttributeNameLimit } && !key.Contains('\0') && value is not null)
                attributes.TryAdd(key, value);
            if (attributes.Count == BirBirSourceFields.AttributeLimit)
                break;
        }
        return attributes;
    }

    private static string? Feature(SortedDictionary<string, string> attributes, string russian, string uzbek) =>
        attributes.FirstOrDefault(pair => pair.Key.Equals(russian, StringComparison.OrdinalIgnoreCase)
            || pair.Key.Equals(uzbek, StringComparison.OrdinalIgnoreCase)).Value;

    private static string[] ReadImages(JsonElement product)
    {
        var image = Property(product, "image");
        var entries = image.ValueKind == JsonValueKind.Array ? image.EnumerateArray().ToArray() : [image];
        return entries.Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).OfType<string>()
            .Where(BirBirListingUrls.IsImage).Distinct(StringComparer.Ordinal).Take(BirBirSourceFields.ImageLimit).ToArray();
    }

    private static string? ElementText(IDocument document, string id, string? child = null)
    {
        string selector = $"main [data-testid='{id}']";
        string? text = document.QuerySelector(child is null ? selector : $"{selector} {child}")?.TextContent.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static JsonElement Property(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement node, string name) => Property(node, name) switch
    {
        { ValueKind: JsonValueKind.String } value when !string.IsNullOrWhiteSpace(value.GetString()) => value.GetString(),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null,
    };

    private static decimal? PositiveDecimal(string? value) =>
        decimal.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
        && amount > 0 ? amount : null;

    private static int? WholeNumber(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : null;

    [GeneratedRegex(@"^(?<rooms>[0-9]+)(?:\s*-\s*комнатная|\s+xonali|\s*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RoomNumber();

    [GeneratedRegex(@"\s(?:район|tumani|тумани)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DistrictLabel();

    private static Result<CaptureReadResult> Invalid(string message) =>
        Result<CaptureReadResult>.Fail(AppErrorFactory.Validation(message));
}
