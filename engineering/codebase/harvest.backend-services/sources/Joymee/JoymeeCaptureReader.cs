using System.Globalization;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;

/// <summary>Reads the public listing JSON and emits the URL a renter opens in Joymee.</summary>
public sealed class JoymeeCaptureReader : ICaptureReader, IFetchDestinationPolicy
{
    private const string ParserVersion = "joymee-json@2";
    private const int LongTermRent = 2;
    private const int Apartment = 1;
    private const int Tashkent = 59;
    private const int Active = 1;

    private static readonly Dictionary<string, string> Filters = new(StringComparer.Ordinal)
    {
        ["deal_type"] = "2", ["property_type"] = "1", ["region"] = "59", ["ordering"] = "newest", ["perPagePlain"] = "5",
    };

    /// <inheritdoc />
    public string Key => "joymee-uz";

    /// <inheritdoc />
    public bool Allows(Uri destination) => Safe(destination)
        && (ListingId(destination) is not null || PageNumber(destination) is not null);

    /// <inheritdoc />
    public bool CanRead(FetchedDocument document) => Allows(document.FinalUrl) && document.StatusCode == 200;

    /// <inheritdoc />
    public Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanRead(document) || !Allows(document.RequestUrl))
            return Task.FromResult(Invalid("Use the public Joymee rental catalog or one of its listings."));

        try
        {
            using var json = JsonDocument.Parse(document.Body);
            long? requested = ListingId(document.RequestUrl);
            long? landed = ListingId(document.FinalUrl);
            return Task.FromResult(requested != landed
                ? Invalid("Joymee redirected to another listing or page.")
                : landed is { } id ? ReadListing(document, json.RootElement, id) : Discover(document, json.RootElement));
        }
        catch (JsonException)
        {
            return Task.FromResult(Invalid("Joymee did not return listing JSON."));
        }
    }

    private static Result<CaptureReadResult> Discover(FetchedDocument document, JsonElement root)
    {
        int? page = PageNumber(document.FinalUrl);
        var results = Property(root, "results");
        if (page is not { } current || current != PageNumber(document.RequestUrl)
            || Number(root, "page") != current || results.ValueKind != JsonValueKind.Array)
            return Invalid("Joymee did not identify the requested catalog page.");

        var found = results.EnumerateArray().Select(item => Number(item, "id")).OfType<long>()
            .Where(id => id > 0).Distinct().Select(id => new DiscoveredUrl
            {
                Url = $"https://{JoymeeSourceAdapter.Host}{JoymeeSourceAdapter.Path}{id}/",
                ExternalKey = id.ToString(CultureInfo.InvariantCulture),
            }).ToList();
        if (results.GetArrayLength() > 0 && found.Count == 0)
            return Invalid("Joymee returned listing results without IDs.");
        if (Uri.TryCreate(Text(root, "next"), UriKind.Absolute, out var next) && Safe(next)
            && PageNumber(next) == current + 1)
            found.Add(new() { Url = next.AbsoluteUri, IsListing = true });
        return Result<CaptureReadResult>.Ok(new() { Discovered = found, Complete = false });
    }

    private Result<CaptureReadResult> ReadListing(FetchedDocument document, JsonElement root, long id)
    {
        if (Number(root, "id") != id || Text(root, "title") is not { } title)
            return Invalid("Joymee did not identify the requested listing.");
        var region = Property(root, "region");
        if (Number(root, "status") != Active || Number(root, "deal_type") != LongTermRent
            || Number(root, "property_type") != Apartment || Number(region, "id") != Tashkent)
            return Result<CaptureReadResult>.Ok(new() { Complete = false });

        string externalKey = id.ToString(CultureInfo.InvariantCulture);
        string url = $"https://joymee.uz/announcements/{externalKey}";
        var text = Text(root, "description");
        var price = Property(root, "pricing");
        var amount = Text(price, "price");
        var currency = Number(price, "currency") switch { 1 => "UZS", 2 => "USD", _ => null };
        var priceText = amount is not null ? string.Join(' ', new[] { amount, currency }.OfType<string>()) : null;
        var district = Text(Property(root, "district"), "name");
        var seller = Property(root, "seller");
        var detail = Property(root, "detail");
        var location = Property(root, "location");
        var sellerName = string.Join(' ', new[] { Text(seller, "first_name"), Text(seller, "last_name") }.OfType<string>());
        string? postedAtText = Text(root, "ads_at");
        var published = DateTimeOffset.TryParse(postedAtText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime() : (DateTimeOffset?)null;
        var attributes = Attributes(detail);
        var (images, media) = ImagesAndMedia(root);

        var metadata = JsonSerializer.SerializeToElement(new
        {
            reader = "page", source = Key, title, text,
            sourceFields = new
            {
                schema = JoymeeSourceFields.Schema, parserVersion = ParserVersion,
                fields = new
                {
                    title, description = text, priceText, postedAtText, sellerName, city = Text(region, "name"), district,
                    imageUrls = images, attributes,
                    externalId = externalKey,
                    addressLine = String(root, "address_line"),
                    advertiserType = NumberOrText(root, "advertiser_type"),
                    publisherVerified = Boolean(seller, "is_verified"),
                    previousPrice = NumberOrText(root, "previous_price"),
                    category = NumberOrText(root, "category"),
                    dealType = NumberOrText(root, "deal_type"),
                    propertyType = NumberOrText(root, "property_type"),
                    detail = detail.ValueKind == JsonValueKind.Object ? new
                    {
                        apartment_type = NumberOrText(detail, "apartment_type"),
                        area_m2 = NumberOrText(detail, "area_m2"),
                        floor_number = NumberOrText(detail, "floor_number"),
                        floors_count = NumberOrText(detail, "floors_count"),
                        repair = NumberOrText(detail, "repair"),
                        room_quantity = NumberOrText(detail, "room_quantity"),
                    } : null,
                    pricing = price.ValueKind == JsonValueKind.Object ? new
                    {
                        currency = NumberOrText(price, "currency"),
                        currency_display = String(price, "currency_display"),
                        price = NumberOrText(price, "price"),
                        price_from = NumberOrText(price, "price_from"),
                        price_to = NumberOrText(price, "price_to"),
                    } : null,
                    location = location.ValueKind == JsonValueKind.Object ? new
                    {
                        latitude = NumberOrText(location, "latitude"),
                        longitude = NumberOrText(location, "longitude"),
                    } : null,
                    media,
                },
            },
        });
        var product = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["@type"] = "Product", ["name"] = title, ["description"] = text, ["url"] = url, ["image"] = images,
            ["offers"] = new { price = amount, priceCurrency = currency },
        });
        var capture = new Capture
        {
            ExternalKey = externalKey, Url = url, OriginKey = "joymee.uz:ad:" + externalKey,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            CapturedAtUtc = document.FetchedAtUtc, PublishedAtUtc = published,
            HttpStatus = document.StatusCode, ContentType = document.ContentType, Body = document.Body,
            ContentProjection = JsonSerializer.Serialize(new { externalKey, product, metadata }),
            Structured = [product, metadata],
            Media = images.Select(image => new CaptureMedia { Url = image, Kind = MediaKind.Image }).ToArray(),
        };
        return Result<CaptureReadResult>.Ok(new() { Captures = [capture], Complete = false });
    }

    private static Dictionary<string, string> Attributes(JsonElement detail)
    {
        var result = new Dictionary<string, string>
        {
            ["Тип сделки"] = "Долгосрочная аренда",
            ["Тип объекта"] = "Квартира",
        };
        foreach (var (key, label) in new[]
        {
            ("room_quantity", "Количество комнат"), ("area_m2", "Общая площадь, м²"),
            ("floor_number", "Этаж"), ("floors_count", "Этажность дома"),
        })
            if (Text(detail, key) is { } text)
                result[label] = text;
        return result;
    }

    private static (string[] Images, IReadOnlyList<object> Media) ImagesAndMedia(JsonElement root)
    {
        var items = Property(root, "media");
        if (items.ValueKind != JsonValueKind.Array)
            return ([], []);

        var images = new List<string>();
        var media = new List<object>();
        foreach (var item in items.EnumerateArray())
        {
            var file = Property(item, "file");
            var url = String(file, "url");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var image) || image.Scheme != Uri.UriSchemeHttps
                || image.IdnHost != "cdn.joymeemedia.uz" || image.UserInfo.Length != 0 || !image.IsDefaultPort)
                continue;

            images.Add(url!);
            media.Add(new
            {
                file = new { id = NumberOrText(file, "id"), url },
                order = NumberOrText(item, "order"),
                is_cover = Boolean(item, "is_cover"),
            });
        }
        return ([.. images.Distinct(StringComparer.Ordinal)], media);
    }

    private static bool Safe(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && uri.IdnHost == JoymeeSourceAdapter.Host && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static long? ListingId(Uri uri)
    {
        if (uri.Query.Length != 0 || !uri.AbsolutePath.StartsWith(JoymeeSourceAdapter.Path, StringComparison.Ordinal))
            return null;
        var value = uri.AbsolutePath[JoymeeSourceAdapter.Path.Length..].TrimEnd('/');
        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long id) && id > 0 ? id : null;
    }

    private static int? PageNumber(Uri uri)
    {
        if (uri.AbsolutePath != JoymeeSourceAdapter.Path)
            return null;
        var parameters = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        if (parameters.Length != Filters.Count + 1)
            return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int page = 0;
        foreach (var parameter in parameters)
        {
            var parts = parameter.Split('=', 2);
            if (parts.Length != 2 || !seen.Add(parts[0]))
                return null;
            if (parts[0] == "page")
            {
                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)
                    return null;
            }
            else if (!Filters.TryGetValue(parts[0], out var expected) || parts[1] != expected)
                return null;
        }
        return page > 0 ? page : null;
    }

    private static JsonElement Property(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) ? value : default;

    private static string? Text(JsonElement node, string name) => Property(node, name) switch
    {
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        { ValueKind: JsonValueKind.String } value when !string.IsNullOrWhiteSpace(value.GetString()) => value.GetString(),
        _ => null,
    };

    private static string? String(JsonElement node, string name) =>
        Property(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? NumberOrText(JsonElement node, string name) => Property(node, name) switch
    {
        { ValueKind: JsonValueKind.Number or JsonValueKind.String } value => value,
        _ => null,
    };

    private static bool? Boolean(JsonElement node, string name) => Property(node, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static long? Number(JsonElement node, string name) =>
        long.TryParse(Text(node, name), NumberStyles.None, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static Result<CaptureReadResult> Invalid(string message) =>
        Result<CaptureReadResult>.Fail(AppErrorFactory.Validation(message));
}
