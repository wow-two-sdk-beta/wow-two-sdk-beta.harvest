using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Typed real-estate facts carried in <c>wow2.post.v1</c>'s listing extra. The post and its links carry identity, text and media.</summary>
public sealed record ListingProjection
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonElement Contract = BuildSchema();

    /// <summary>Gets the versioned projection schema identifier.</summary>
    public string Schema { get; init; } = PostEnvelopeConstants.ListingSchema;
    /// <summary>Gets the property above the individual unit.</summary>
    public required ListingProperty Property { get; init; }
    /// <summary>Gets measurements, room counts and layout of the offered unit.</summary>
    public required ListingUnit Unit { get; init; }
    /// <summary>Gets published commercial terms.</summary>
    public IReadOnlyList<ListingOffer> Offers { get; init; } = [];
    /// <summary>Gets the published geographic evidence.</summary>
    public ListingPlace? Place { get; init; }
    /// <summary>Gets observed features by scope, independently of features the publisher did not mention.</summary>
    public IReadOnlyList<ListingFeature> Features { get; init; } = [];
    /// <summary>Gets publisher attributes with their original scope.</summary>
    public ListingPublisher? Publisher { get; init; }
    /// <summary>Gets the native schema and deterministic mapping version that produced these facts.</summary>
    public required ListingMapping Mapping { get; init; }
    /// <summary>Gets each value's source within the capture.</summary>
    public IReadOnlyList<ListingEvidence> Evidence { get; init; } = [];
    /// <summary>Gets present native values that could not be normalized.</summary>
    public IReadOnlyList<ListingUnknownValue> UnknownValues { get; init; } = [];
    /// <summary>Gets data quality findings from the deterministic pass.</summary>
    public IReadOnlyList<string> QualityFlags { get; init; } = [];

    /// <summary>Gets the JSON Schema for the serialized projection.</summary>
    public static JsonElement JsonSchema => Contract;

    /// <summary>Serializes the projection using its stable field names and enum values.</summary>
    /// <returns>The listing wire object.</returns>
    public JsonElement ToJson() => JsonSerializer.SerializeToElement(this, Wire);

    /// <summary>Reads a projection of the supported version, tolerating malformed or older extension data.</summary>
    /// <param name="value">The contents of a post's listing extra.</param>
    /// <returns>The typed value when recognized.</returns>
    public static ListingProjection? Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("schema", out var schema)
            || schema.ValueKind != JsonValueKind.String || schema.GetString() != PostEnvelopeConstants.ListingSchema)
            return null;
        try
        {
            var projection = value.Deserialize<ListingProjection>(Wire);
            return projection is { Property: not null, Unit: not null, Mapping: not null, Offers: not null } ? projection : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement BuildSchema()
    {
        var node = Wire.GetJsonSchemaAsNode(typeof(ListingProjection)).AsObject();
        node["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        var properties = node["properties"]!.AsObject();
        properties["schema"] = new JsonObject { ["const"] = PostEnvelopeConstants.ListingSchema };
        if (node["required"] is JsonArray required) required.Add("schema");
        else node["required"] = new JsonArray(JsonValue.Create("schema"));
        return JsonSerializer.SerializeToElement(node);
    }
}

/// <summary>Published real-estate characteristics.</summary>
/// <param name="Kind">The property kind, independent of construction or market.</param>
/// <param name="MarketSegment">The new-build or resale segment.</param>
/// <param name="Construction">The published construction type.</param>
/// <param name="BuildYear">A year or year range with its source role.</param>
/// <param name="Finishing">The finish stage.</param>
/// <param name="Renovation">The stated renovation descriptor.</param>
public sealed record ListingProperty(ListingPropertyKind? Kind = null, ListingMarketSegment? MarketSegment = null,
    ListingConstruction? Construction = null, ListingBuildYear? BuildYear = null,
    ListingFinishing? Finishing = null, ListingRenovation? Renovation = null);

/// <summary>Unit counts and metric measurements, absent when the source does not supply them.</summary>
/// <param name="Rooms">The source's room count.</param>
/// <param name="Floor">The signed unit floor.</param>
/// <param name="BuildingFloors">The source's level count for the building.</param>
/// <param name="TotalAreaM2">The total area in square metres.</param>
/// <param name="LivingAreaM2">The stated living area in square metres.</param>
/// <param name="KitchenAreaM2">The stated kitchen area in square metres.</param>
/// <param name="LotAreaM2">The stated lot area in square metres.</param>
/// <param name="CeilingHeightM">The ceiling height in metres.</param>
/// <param name="BathroomArrangement">The reported bathroom arrangement.</param>
/// <param name="BathroomsMin">The stated lower bound on bathrooms.</param>
/// <param name="Furnishing">Whether the unit is furnished.</param>
/// <param name="Layout">The unit style.</param>
/// <param name="RoomConnectivity">Whether rooms are separate or adjacent.</param>
public sealed record ListingUnit(short? Rooms = null, short? Floor = null, short? BuildingFloors = null,
    decimal? TotalAreaM2 = null, decimal? LivingAreaM2 = null, decimal? KitchenAreaM2 = null, decimal? LotAreaM2 = null,
    decimal? CeilingHeightM = null, ListingBathroomArrangement? BathroomArrangement = null, short? BathroomsMin = null,
    ListingFurnishing? Furnishing = null, ListingLayout? Layout = null, ListingRoomConnectivity? RoomConnectivity = null);

/// <summary>A source-stated offer and its separately stated terms.</summary>
/// <param name="Transaction">The transaction kind.</param>
/// <param name="Period">The time basis of the asking price.</param>
/// <param name="Price">The stated asking price.</param>
/// <param name="PreviousPrice">An earlier asking price explicitly provided by the source.</param>
/// <param name="Negotiable">The published negotiability flag.</param>
/// <param name="HasCommission">The commission applicability flag.</param>
public sealed record ListingOffer(ListingTransaction? Transaction = null, ListingPeriod? Period = null,
    ListingMoney? Price = null, ListingMoney? PreviousPrice = null, bool? Negotiable = null, bool? HasCommission = null);

/// <summary>Exact decimal money with optional currency and range.</summary>
/// <param name="Amount">The single amount, when stated.</param>
/// <param name="Currency">An explicit supported ISO 4217 currency code.</param>
/// <param name="Raw">Original price display text when supplied.</param>
/// <param name="Min">A lower amount bound.</param>
/// <param name="Max">An upper amount bound.</param>
public sealed record ListingMoney(decimal? Amount, string? Currency, string? Raw = null, decimal? Min = null, decimal? Max = null);

/// <summary>A year interval with the meaning supplied by the source.</summary>
/// <param name="From">The inclusive first year.</param>
/// <param name="To">The inclusive last year.</param>
/// <param name="Role">The kind of date represented.</param>
public sealed record ListingBuildYear(short? From, short? To, ListingBuildYearRole Role);

/// <summary>Place labels and a provider-supplied point. Coordinates describe the ad's marker.</summary>
/// <param name="City">The published city.</param>
/// <param name="District">The published district.</param>
/// <param name="Address">The published address label.</param>
/// <param name="Latitude">The WGS 84 latitude.</param>
/// <param name="Longitude">The WGS 84 longitude.</param>
public sealed record ListingPlace(string? City, string? District, string? Address, decimal? Latitude, decimal? Longitude);

/// <summary>A feature observation at a particular scope.</summary>
/// <param name="Code">The shared feature code.</param>
/// <param name="Scope">The part of the property or its surroundings the feature describes.</param>
/// <param name="Present">Whether the feature is explicitly reported present.</param>
public sealed record ListingFeature(string Code, ListingFeatureScope Scope, bool Present);

/// <summary>Publisher signals supplied by the marketplace.</summary>
/// <param name="AccountClass">The marketplace account class when understood.</param>
/// <param name="Verified">The marketplace verification marker, where supplied.</param>
public sealed record ListingPublisher(ListingAccountClass? AccountClass, bool? Verified);

/// <summary>Identifies the native input and versioned mapper.</summary>
/// <param name="SourceSchema">The producer's native schema identifier.</param>
/// <param name="Parser">The source parser version.</param>
/// <param name="Version">The deterministic mapping rule version.</param>
public sealed record ListingMapping(string SourceSchema, string? Parser, string Version);

/// <summary>Evidence for one normalized value.</summary>
/// <param name="Path">The listing's JSON pointer for the fact.</param>
/// <param name="SourcePath">The JSON pointer in the source field envelope or the corresponding post input.</param>
/// <param name="Raw">The original value.</param>
/// <param name="Method">How the value was selected or converted.</param>
public sealed record ListingEvidence(string Path, string SourcePath, string Raw, string Method);

/// <summary>A native value with no supported normalization.</summary>
/// <param name="Path">The target fact's JSON pointer.</param>
/// <param name="SourcePath">The pointer to the input field.</param>
/// <param name="Raw">The native value.</param>
/// <param name="Reason">The reason the mapping could not be made.</param>
public sealed record ListingUnknownValue(string Path, string SourcePath, string Raw, string Reason);
