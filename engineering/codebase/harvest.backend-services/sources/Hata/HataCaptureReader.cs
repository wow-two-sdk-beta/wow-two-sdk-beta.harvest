using System.Globalization;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Hata;

/// <summary>Reads HATA's rental cards and the facts published on the corresponding detail pages.</summary>
public sealed class HataCaptureReader : ICaptureReader, IFetchDestinationPolicy
{
    private const string ParserVersion = "hata-html@1";
    private const string ListingPath = "/listings/object/";

    /// <inheritdoc />
    public string Key => "hata-uz";

    /// <inheritdoc />
    public bool Allows(Uri destination)
    {
        return IsPublic(destination) && (CatalogPage(destination) is not null || ListingId(destination) is not null);
    }

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document)
    {
        return document.StatusCode == 200
            && document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true
            && Allows(document.FinalUrl);
    }

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(
        FetchedDocument document,
        CancellationToken cancellationToken = default)
    {
        if (!CanRead(document) || !Allows(document.RequestUrl))
            return Invalid("Use the public HATA apartment rental catalog or a listing detail page.");

        string? id = ListingId(document.FinalUrl);
        if (id != ListingId(document.RequestUrl) || CatalogPage(document.RequestUrl) != CatalogPage(document.FinalUrl))
            return Invalid("HATA redirected to another listing or catalog page.");

        IDocument html = await new HtmlParser().ParseDocumentAsync(
            Encoding.UTF8.GetString(document.Body.Span), cancellationToken);
        return id is null ? Discover(document.FinalUrl, html) : ReadListing(document, html, id);
    }

    private static Result<CaptureReadResult> Discover(Uri page, IDocument html)
    {
        var discovered = new List<DiscoveredUrl>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (IElement anchor in html.QuerySelectorAll("article a[href]"))
        {
            if (Resolve(page, anchor.GetAttribute("href")) is not { } url || ListingId(url) is not { } id || !seen.Add(id))
                continue;
            discovered.Add(new() { Url = url.AbsoluteUri, ExternalKey = id });
        }
        if (discovered.Count == 0)
            return Invalid("The HATA catalog response contains no identifiable rental cards.");

        int? nextPage = CatalogPage(page) + 1;
        Uri? next = html.QuerySelectorAll("a[href]").Select(anchor => Resolve(page, anchor.GetAttribute("href")))
            .FirstOrDefault(url => url is not null && CatalogPage(url) == nextPage);
        if (next is not null)
            discovered.Add(new() { Url = next.AbsoluteUri, IsListing = true });

        return Result<CaptureReadResult>.Ok(new() { Discovered = discovered });
    }

    private Result<CaptureReadResult> ReadListing(FetchedDocument document, IDocument html, string id)
    {
        JsonElement[] nodes = html.QuerySelectorAll("script[type='application/ld+json']")
            .Select(script => Parse(script.TextContent)).ToArray();
        JsonElement listing = nodes.FirstOrDefault(node => Scalar(node, "@type") == "RealEstateListing");
        JsonElement apartment = Property(listing, "about");
        JsonElement address = Property(apartment, "address");
        string? city = Scalar(address, "addressLocality");
        Uri? url = Resolve(document.FinalUrl, Scalar(listing, "url"));
        if (url is null || ListingId(url) != id || Scalar(apartment, "@type") != "Apartment"
            || city is not ("Ташкент" or "Tashkent" or "Toshkent") || !HasRentalBreadcrumb(document.FinalUrl, nodes)
            || Scalar(listing, "name") is not { } title)
            return Invalid("The detail did not identify the requested Tashkent rental apartment.");

        IElement? descriptionHeading = Heading(html, "Описание");
        string? description = descriptionHeading?.NextElementSibling?.TextContent.Trim();
        if (string.IsNullOrEmpty(description))
            description = Scalar(listing, "description");
        JsonElement offer = Property(listing, "offers");
        string? amount = Scalar(offer, "price");
        string? currency = Scalar(offer, "priceCurrency");
        string? priceText = amount is null ? null : string.Join(" ", new[] { amount, currency }.OfType<string>());
        string? district = Scalar(address, "addressRegion");
        string? addressLine = Scalar(address, "streetAddress");
        string? rooms = Scalar(apartment, "numberOfRooms");
        string? floor = Scalar(apartment, "floorLevel");
        string? area = Scalar(Property(apartment, "floorSize"), "value");
        string[] images = Items(Property(apartment, "image")).Select(image => image.ValueKind == JsonValueKind.String
                && Uri.TryCreate(image.GetString(), UriKind.Absolute, out var imageUrl)
                && imageUrl.Scheme == Uri.UriSchemeHttps && imageUrl.IsDefaultPort && imageUrl.UserInfo.Length == 0
                    ? imageUrl.AbsoluteUri : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        Dictionary<string, string> attributes = Attributes(html);
        if (rooms is not null) attributes.TryAdd("Количество комнат", rooms);
        if (floor is not null) attributes.TryAdd("Этаж", floor);
        if (area is not null) attributes.TryAdd("Общая площадь, м²", area);
        var fields = new
        {
            externalId = id,
            title,
            description,
            priceText,
            city,
            district,
            addressLine,
            imageUrls = images,
            attributes,
            rooms,
            floor,
            areaM2 = area,
            yearBuilt = Scalar(apartment, "yearBuilt"),
            availability = Scalar(offer, "availability"),
        };
        JsonElement metadata = JsonSerializer.SerializeToElement(new
        {
            reader = "page", source = Key,
            sourceFields = new { schema = "hata.ad.v1", parserVersion = ParserVersion, fields },
        });
        JsonElement product = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["@type"] = "Product",
            ["name"] = title,
            ["description"] = description,
            ["url"] = url.AbsoluteUri,
            ["image"] = images,
            ["offers"] = new { price = amount, priceCurrency = currency },
            ["location"] = new { address, geo = NullableProperty(apartment, "geo") },
        });
        var capture = new Capture
        {
            ExternalKey = id,
            Url = url.AbsoluteUri,
            OriginKey = $"{HataSourceAdapter.Host}:ad:{id}",
            RequestUrl = document.RequestUrl.AbsoluteUri,
            FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode,
            CapturedAtUtc = document.FetchedAtUtc,
            ContentType = document.ContentType,
            Body = document.Body,
            Locale = Scalar(listing, "inLanguage"),
            ContentProjection = JsonSerializer.Serialize(new { parserVersion = ParserVersion, fields, product }),
            Structured = [product, metadata],
            Media = images.Select((image, index) => new CaptureMedia
            {
                Url = image, Kind = MediaKind.Image, Position = index,
            }).ToArray(),
        };
        return Result<CaptureReadResult>.Ok(new() { Captures = [capture] });
    }

    private static IElement? Heading(IDocument html, string label)
    {
        return html.QuerySelectorAll(".property-detail h2")
            .FirstOrDefault(heading => heading.TextContent.Trim() == label);
    }

    private static Dictionary<string, string> Attributes(IDocument html)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Тип сделки"] = "Долгосрочная аренда",
            ["Тип объекта"] = "Квартира",
        };
        IElement? grid = Heading(html, "Характеристики")?.NextElementSibling;
        if (grid is null) return values;
        foreach (IElement pair in grid.Children)
        {
            string? label = pair.FirstElementChild?.TextContent.Trim();
            string value = string.Join(" ", pair.Children.Skip(1).Select(child => child.TextContent.Trim()));
            if (!string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(value))
                values.TryAdd(label, value);
        }
        return values;
    }

    private static bool HasRentalBreadcrumb(Uri page, IEnumerable<JsonElement> nodes)
    {
        return nodes.Where(node => Scalar(node, "@type") == "BreadcrumbList")
            .SelectMany(node => Items(Property(node, "itemListElement")))
            .Select(item => Resolve(page, Scalar(item, "item")))
            .Any(url => url?.AbsolutePath.StartsWith("/listings/rent/", StringComparison.Ordinal) == true);
    }

    private static Uri? Resolve(Uri page, string? href)
    {
        return !string.IsNullOrWhiteSpace(href) && Uri.TryCreate(page, href, out var url) && IsPublic(url) ? url : null;
    }

    private static bool IsPublic(Uri url)
    {
        return url.IsAbsoluteUri && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort
            && url.IdnHost == HataSourceAdapter.Host && url.UserInfo.Length == 0 && url.Fragment.Length == 0;
    }

    private static int? CatalogPage(Uri url)
    {
        if (url.AbsolutePath != HataSourceAdapter.CatalogPath) return null;
        if (url.Query.Length == 0) return 1;
        return url.Query.StartsWith("?page=", StringComparison.Ordinal)
            && int.TryParse(url.Query.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out int page) && page > 0
            ? page : null;
    }

    private static string? ListingId(Uri url)
    {
        if (url.Query.Length != 0 || url.Segments.Length != 4 || !url.AbsolutePath.StartsWith(ListingPath, StringComparison.Ordinal))
            return null;
        ReadOnlySpan<char> slug = url.Segments[3];
        int separator = slug.LastIndexOf('-');
        if (separator <= 0) return null;
        ReadOnlySpan<char> id = slug[(separator + 1)..];
        return long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out long number) && number > 0
            ? number.ToString(CultureInfo.InvariantCulture) : null;
    }

    private static JsonElement Parse(string text)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(text);
            return json.RootElement.Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static JsonElement Property(JsonElement node, string key)
    {
        return node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    }

    private static JsonElement? NullableProperty(JsonElement node, string key)
    {
        JsonElement value = Property(node, key);
        return value.ValueKind == JsonValueKind.Undefined ? null : value;
    }

    private static string? Scalar(JsonElement node, string key)
    {
        JsonElement value = Property(node, key);
        return value.ValueKind switch
        {
            JsonValueKind.String when value.GetString()?.Trim() is { Length: > 0 } text => text,
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static IEnumerable<JsonElement> Items(JsonElement array)
    {
        return array.ValueKind == JsonValueKind.Array ? array.EnumerateArray() : [];
    }

    private static Result<CaptureReadResult> Invalid(string message)
    {
        return Result<CaptureReadResult>.Fail(AppErrorFactory.Validation(message));
    }

}
