using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;

/// <summary>The versioned fields for a BirBir apartment rental.</summary>
public sealed record BirBirSourceFields
{
    /// <summary>The native schema identifier read by SCFD consumers.</summary>
    public const string Schema = "birbir.ad.v1";
    internal const int IdLimit = 128;
    internal const int UrlLimit = 2048;
    internal const int TitleLimit = 500;
    internal const int DescriptionLimit = 24000;
    internal const int LabelLimit = 256;
    internal const int PriceTextLimit = 128;
    internal const int DateTextLimit = 80;
    internal const int AttributeLimit = 40;
    internal const int AttributeNameLimit = 100;
    internal const int AttributeValueLimit = 500;

    /// <summary>Gets the ad ID as a string, independent of the title, URL slug and locale.</summary>
    [MaxLength(IdLimit)] public required string ExternalId { get; init; }
    /// <summary>Gets the source URL in the language it was read in.</summary>
    [MaxLength(UrlLimit)] public required string Url { get; init; }
    /// <summary>Gets the language of the page, when known.</summary>
    public BirBirLocale? Locale { get; init; }
    /// <summary>Gets the published title.</summary>
    [MaxLength(TitleLimit)] public required string Title { get; init; }
    /// <summary>Gets the description supplied by the source, which may be an excerpt.</summary>
    [MaxLength(DescriptionLimit)] public string? Description { get; init; }
    /// <summary>Gets the price displayed on the listing.</summary>
    [MaxLength(PriceTextLimit)] public string? PriceText { get; init; }
    /// <summary>Gets the listed numeric amount in the stated currency.</summary>
    public decimal? PriceAmount { get; init; }
    /// <summary>Gets the supported currency codes observed on the source.</summary>
    public BirBirCurrency? Currency { get; init; }
    /// <summary>Gets the transaction category from the listing's route.</summary>
    public BirBirTransaction Transaction { get; init; } = BirBirTransaction.Rent;
    /// <summary>Gets the property category from the listing's route.</summary>
    public BirBirPropertyType PropertyType { get; init; } = BirBirPropertyType.Apartment;
    /// <summary>Gets the condition declared by BirBir, if recognized.</summary>
    public BirBirCondition? Condition { get; init; }
    /// <summary>Gets the listing availability declared by BirBir, if recognized.</summary>
    public BirBirAvailability? Availability { get; init; }
    /// <summary>Gets the advertised square metres.</summary>
    public decimal? AreaSquareMetres { get; init; }
    /// <summary>Gets the published room label, which can include names such as studio.</summary>
    [MaxLength(LabelLimit)] public string? RoomsText { get; init; }
    /// <summary>Gets the room count when the label declares one.</summary>
    public int? RoomCount { get; init; }
    /// <summary>Gets the floor claimed for the apartment.</summary>
    public int? Floor { get; init; }
    /// <summary>Gets the number of floors in the building.</summary>
    public int? BuildingFloors { get; init; }
    /// <summary>Gets the source's city label.</summary>
    [MaxLength(LabelLimit)] public string? City { get; init; }
    /// <summary>Gets the ISO country code declared on the listing.</summary>
    [MaxLength(2)] public string? CountryCode { get; init; }
    /// <summary>Gets an identified district, when the source names one as a district.</summary>
    [MaxLength(LabelLimit)] public string? District { get; init; }
    /// <summary>Gets the public location label, which can be a landmark or a district.</summary>
    [MaxLength(LabelLimit)] public string? LocationText { get; init; }
    /// <summary>Gets the full public place label when supplied separately.</summary>
    [MaxLength(LabelLimit)] public string? AddressText { get; init; }
    /// <summary>Gets the public seller display name.</summary>
    [MaxLength(LabelLimit)] public string? SellerName { get; init; }
    /// <summary>Gets the source's publication label.</summary>
    [MaxLength(DateTextLimit)] public string? PostedAtText { get; init; }
    /// <summary>Gets a publication timestamp when the source supplied a date and time.</summary>
    public DateTimeOffset? PublishedAtUtc { get; init; }
    /// <summary>Gets the date on which the structured offer says it became valid.</summary>
    public DateOnly? OfferValidFrom { get; init; }
    /// <summary>Gets the distinct public listing image URLs.</summary>
    public required IReadOnlyList<string> ImageUrls { get; init; }
    /// <summary>Gets published feature labels and values, including labels whose meanings have yet to be reviewed.</summary>
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }

    private static readonly JsonSerializerOptions Wire = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    private sealed record NativeEnvelope(string Schema, string ParserVersion, BirBirSourceFields Fields);

    /// <summary>Gets the listing envelope schema from the same JSON contract used to serialize captures.</summary>
    public static JsonElement JsonSchema { get; } = BuildSchema();

    /// <summary>Serializes the declared listing fields, including nulls for unpublished values.</summary>
    /// <param name="parserVersion">The implementation that produced this reading.</param>
    public JsonElement ToEnvelope(string parserVersion) =>
        JsonSerializer.SerializeToElement(new NativeEnvelope(Schema, parserVersion, this), Wire);

    internal static BirBirLocale LocaleOf(Uri url) =>
        url.AbsolutePath.StartsWith("/uz/", StringComparison.Ordinal) ? BirBirLocale.Uz : BirBirLocale.Ru;

    /// <summary>Checks the canonical form of an imported rental URL against its original ID.</summary>
    public static bool MatchesListing(Uri uri, string id)
    {
        if (!uri.IsAbsoluteUri || uri.IdnHost is not (BirBirSourceAdapter.Host or "www.birbir.uz"))
            return false;
        var canonical = new UriBuilder(uri) { Host = BirBirSourceAdapter.Host, Query = "", Fragment = "" }.Uri;
        return uri.AbsoluteUri.Length <= UrlLimit && BirBirListingUrls.ListingId(canonical) == id;
    }

    /// <summary>Projects the existing operator import onto the same wire contract as a collected listing.</summary>
    public static BirBirSourceFields FromImport(string id, string url, string title, string? description, string? priceText,
        string? city, string? district, string? sellerName, DateTimeOffset? published,
        IReadOnlyDictionary<string, string> attributes, IReadOnlyList<string> images)
    {
        var nativeAttributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, rawValue) in attributes)
        {
            if (!key.Contains('\0'))
                nativeAttributes.Add(key, ReadText(rawValue, AttributeValueLimit) ?? "");
        }
        return new()
        {
            ExternalId = id, Url = url, Title = ReadText(title, TitleLimit)!,
            Locale = LocaleOf(new Uri(url)),
            Description = ReadText(description, DescriptionLimit), PriceText = ReadText(priceText, PriceTextLimit),
            City = ReadText(city, LabelLimit), District = ReadText(district, LabelLimit), SellerName = ReadText(sellerName, LabelLimit),
            PostedAtText = published?.ToString("O"), PublishedAtUtc = published, Attributes = nativeAttributes, ImageUrls = images,
        };
    }

    private static JsonElement BuildSchema()
    {
        var root = (JsonObject)Wire.GetJsonSchemaAsNode(typeof(NativeEnvelope), new()
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (node is JsonObject field && context.PropertyInfo?.AttributeProvider is { } attributes
                    && attributes.GetCustomAttributes(typeof(MaxLengthAttribute), true).FirstOrDefault() is MaxLengthAttribute limit)
                    field[context.PropertyInfo.Name == "imageUrls" ? "maxItems" : "maxLength"] = limit.Length;
                return node;
            },
        });
        root["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        var properties = (JsonObject)root["properties"]!;
        ((JsonObject)properties["schema"]!)["const"] = Schema;
        var fields = (JsonObject)properties["fields"]!;
        fields["required"] = new JsonArray(((JsonObject)fields["properties"]!).Select(p => (JsonNode)JsonValue.Create(p.Key)!).ToArray());
        var fieldProperties = (JsonObject)fields["properties"]!;
        ((JsonObject)fieldProperties["externalId"]!)["pattern"] = "^[0-9]+$";
        ((JsonObject)fieldProperties["url"]!)["format"] = "uri";
        ((JsonObject)fieldProperties["priceAmount"]!)["exclusiveMinimum"] = 0;
        ((JsonObject)fieldProperties["areaSquareMetres"]!)["exclusiveMinimum"] = 0;
        ((JsonObject)fieldProperties["roomCount"]!)["minimum"] = 1;
        ((JsonObject)fieldProperties["buildingFloors"]!)["minimum"] = 1;
        var image = (JsonObject)((JsonObject)fieldProperties["imageUrls"]!)["items"]!;
        image["maxLength"] = UrlLimit;
        image["format"] = "uri";
        var attributes = (JsonObject)fieldProperties["attributes"]!;
        attributes["maxProperties"] = AttributeLimit;
        attributes["propertyNames"] = new JsonObject { ["maxLength"] = AttributeNameLimit };
        ((JsonObject)attributes["additionalProperties"]!)["maxLength"] = AttributeValueLimit;
        return JsonSerializer.SerializeToElement(root);
    }

    internal static string? ReadText(string? value, int limit)
    {
        var text = value?.Replace("\0", "\uFFFD", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.Length <= limit)
            return text;
        return text[..(char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit)];
    }
}

/// <summary>The languages supported by the rental reader.</summary>
public enum BirBirLocale
{
    /// <summary>Russian.</summary>
    Ru,
    /// <summary>Uzbek.</summary>
    Uz,
}

/// <summary>Currency codes observed in BirBir's apartment rental listing data.</summary>
public enum BirBirCurrency
{
    /// <summary>Uzbekistani som.</summary>
    [JsonStringEnumMemberName("UZS")]
    Uzs,
    /// <summary>US dollar.</summary>
    [JsonStringEnumMemberName("USD")]
    Usd,
}

/// <summary>The supported transaction category.</summary>
public enum BirBirTransaction
{
    /// <summary>Rent, without a source-declared rental period.</summary>
    Rent,
}

/// <summary>The supported property category.</summary>
public enum BirBirPropertyType
{
    /// <summary>Apartment.</summary>
    Apartment,
}

/// <summary>Condition codes declared in the listing's structured data.</summary>
public enum BirBirCondition
{
    /// <summary>New.</summary>
    New,
    /// <summary>Used.</summary>
    Used,
}

/// <summary>Availability codes declared in the listing's structured data.</summary>
public enum BirBirAvailability
{
    /// <summary>The listing declares the offer available.</summary>
    InStock,
    /// <summary>The listing declares the offer unavailable.</summary>
    OutOfStock,
}
