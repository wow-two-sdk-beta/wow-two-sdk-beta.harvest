using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>The code-owned answer schemas of <c>extract-event</c> and <c>extract-listing</c>, generated from the answer
/// records: every field required, no extra properties, the agreed enums, and the rental terms of <c>rental-terms@2</c>.</summary>
public sealed class PostExtractionDefinitionsTests
{
    [Fact]
    public void EventSchema_ShouldRequireEveryField_AndDisallowOthers()
    {
        var schema = Parse(PostExtractionDefinitions.EventSchema);

        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        Names(schema["required"]).Should().BeEquivalentTo("name", "editionLabel", "occurrences", "place", "organizer", "format", "access",
            "prices", "registration", "participation", "status", "relation", "fashion", "flags");
        var occurrence = schema["properties"]!["occurrences"]!["items"]!;
        Names(occurrence["required"]).Should().BeEquivalentTo("starts", "ends", "precision", "certainty", "evidence");
        Names(occurrence["properties"]!["certainty"]!["enum"]).Should().Equal("stated", "estimated");
        Names(schema["properties"]!["name"]!["required"]).Should().BeEquivalentTo("en", "original");
        Names(schema["properties"]!["place"]!["required"]).Should().BeEquivalentTo("venue", "city", "country");
    }

    [Fact]
    public void EventSchema_ShouldListTheAgreedValues_WithAnUnsureWayOut()
    {
        var properties = Parse(PostExtractionDefinitions.EventSchema)["properties"]!;

        Names(properties["format"]!["enum"]).Should().Equal("expo", "trade-fair", "runway", "festival", "competition", "conference",
            "market", "exhibition", "workshop", "concert", "other", "unsure");
        Names(properties["access"]!["enum"]).Should().Equal("free", "ticketed", "application", "unknown");
        Names(properties["status"]!["enum"]).Should().Equal("scheduled", "cancelled", "postponed", "held", "unsure");
        Names(properties["participation"]!["items"]!["enum"]).Should().Equal("exhibitor", "visitor", "contestant", "speaker", "unsure");
        var relation = properties["relation"]!["properties"]!;
        Names(relation["role"]!["enum"]).Should().Equal("announcement", "update", "report", "mention", "unsure");
        Names(relation["changes"]!["items"]!["enum"]).Should().Equal("dates", "place", "price", "program", "registration", "deadline",
            "results", "status", "unsure");
        Names(relation["status"]!["enum"]).Should().Contain(["cancelled", "postponed", "sold-out", "withdrawn"]);
        var fashion = properties["fashion"]!;
        Names(fashion["type"]).Should().Contain("null", "the block is empty outside style-fashion");
        Names(fashion["properties"]!["segments"]!["items"]!["enum"]).Should().Equal("apparel", "textile", "accessories", "jewelry",
            "footwear", "handmade", "beauty", "unsure");
    }

    [Fact]
    public void ListingSchema_ShouldRequireEveryField_WithTheVersionTwoGroups()
    {
        var schema = Parse(PostExtractionDefinitions.ListingSchema);
        var properties = schema["properties"]!;

        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        Names(schema["required"]).Should().BeEquivalentTo("title", "categoryPath", "propertyKind", "invalid", "offerType", "price", "item",
            "furnished", "apartmentAmenities", "nearbyAmenities", "location", "landmarks", "author", "noAgents", "relation", "rentalTerms",
            "acceptedTenants", "excludedTenants", "urgent", "flags");
        Names(properties["offerType"]!["enum"]).Should().Equal("rent", "sale", "rent-or-sale", "exchange", "rent-to-own", "unsure");
        Names(properties["propertyKind"]!["enum"]).Should().Equal("apartment", "house", "room", "bed-space", "commercial", "land", "unsure", null);
        Names(properties["invalid"]!["enum"]).Should().Equal("not-a-property", "multi-listing", null);
        Names(properties["furnished"]!["enum"]).Should().Equal("yes", "partly", "no", "unknown", "unsure");
        Names(properties["author"]!["required"]).Should().Equal("kind");
        Names(properties["author"]!["properties"]!["kind"]!["enum"]).Should().Equal("owner", "agent", "agency", "developer", "management-company",
            "unknown", "unsure");
        Names(properties["price"]!["required"]).Should().BeEquivalentTo("amount", "currency", "period", "negotiable", "evidence");
        Names(properties["price"]!["properties"]!["currency"]!["enum"]).Should().Equal("usd", "uzs", "unsure");
        Names(properties["price"]!["properties"]!["period"]!["enum"]).Should().Equal("hour", "night", "day", "week", "month", "year", "unsure", null);
        Names(properties["location"]!["required"]).Should().BeEquivalentTo("district", "city", "address");

        var item = properties["item"]!;
        Names(item["required"]).Should().BeEquivalentTo("propertyType", "buildingType", "yearBuilt", "rooms", "layout", "areaM2", "floor", "floors",
            "ceilingM", "bathrooms", "renovation", "building");
        Names(item["properties"]!["areaM2"]!["required"]).Should().BeEquivalentTo("total", "living", "kitchen");
        Names(item["properties"]!["bathrooms"]!["required"]).Should().BeEquivalentTo("count", "type");
        Names(item["properties"]!["propertyType"]!["enum"]).Should().Equal("new-build", "secondary", "unsure", null);
        Names(item["properties"]!["renovation"]!["enum"]).Should().Equal("shell", "rough-finish", "needs-renovation", "with-renovation", "euro",
            "designer", "unsure", null);
        item["properties"]!.AsObject().Select(property => property.Key).Should().NotContain(["brand", "model", "year"]);

        Names(properties["acceptedTenants"]!["items"]!["enum"]).Should().Contain(["family", "expats", "students", "animals", "unsure"]);
        Names(properties["apartmentAmenities"]!["items"]!["enum"]).Should().Contain(["air-conditioner", "washing-machine", "unsure"]);
        Names(properties["landmarks"]!["items"]!["required"]).Should().BeEquivalentTo("type", "name", "evidence");
    }

    [Fact]
    public void ListingSchema_ShouldCarryTheFiveRentalTerms_WithEvidenceAndLowercaseUnits()
    {
        var terms = Parse(PostExtractionDefinitions.ListingSchema)["properties"]!["rentalTerms"]!.AsObject();

        Names(terms["type"]).Should().Contain("null");
        Names(terms["required"]).Should().BeEquivalentTo("deposit", "advanceRent", "utilities", "minimumStay", "commission");
        foreach (var term in new[] { "deposit", "advanceRent", "utilities", "minimumStay", "commission" })
        {
            var node = terms["properties"]![term]!;
            Names(node["required"]).Should().BeEquivalentTo("status", "claims");
            Names(node["properties"]!["status"]!["enum"]).Should().Equal("unknown", "absent", "present", "conflict");
            Names(node["properties"]!["claims"]!["items"]!["required"]).Should().BeEquivalentTo("evidence", "value");
        }
        var commission = terms["properties"]!["commission"]!["properties"]!["claims"]!["items"]!["properties"]!["value"]!;
        Names(commission["required"]).Should().BeEquivalentTo("amount", "currency", "percent", "rentMultiple");
        Names(commission["properties"]!["currency"]!["enum"]).Should().Equal("usd", "uzs", "unsure", null);
        var stay = terms["properties"]!["minimumStay"]!["properties"]!["claims"]!["items"]!["properties"]!["value"]!["properties"]!["unit"]!;
        Names(stay["enum"]).Should().Equal("hour", "night", "day", "week", "month", "year", "unsure");
    }

    [Fact]
    public void ListingSchema_ShouldHoldNoContactField_AndAllowNoOtherProperty()
    {
        var schema = Parse(PostExtractionDefinitions.ListingSchema);
        var names = new List<string>();
        var open = new List<string>();
        Walk(schema, "$", names, open);

        open.Should().BeEmpty("every object of the schema disallows other properties");
        names.Should().NotContain(name => ContactWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)),
            "no field names a person, a phone or another contact, and none holds a registration date");
        names.Should().NotContain(["authorName", "phoneNumbers", "authorRegisteredAt", "postedAt"]);
    }

    [Fact]
    public void Variables_ShouldCarryThePostFieldsAndItsTags_AndThreeMoreForAListing()
    {
        PostExtractionDefinitions.Variables.Should().Contain(["title", "text", "dates", "place", "prices", "postedAt", "entityType", "domains",
            "about", "timeframe", "titleEn"]);
        PostExtractionDefinitions.Variables.Should().NotContain(["attributes", "section", "seller"]);
        PostExtractionDefinitions.VariablesOf("extract-event").Should().Equal([.. PostExtractionDefinitions.Variables, "eventFormats"]);
        PostExtractionDefinitions.VariablesOf("extract-listing").Should().Equal([.. PostExtractionDefinitions.Variables, "attributes", "section", "seller"]);
        PostExtractionDefinitions.VariablesOf("post-tags").Should().BeEmpty();
        PostExtractionDefinitions.OutputSchema("extract-event").Should().Be(PostExtractionDefinitions.EventSchema);
        PostExtractionDefinitions.OutputSchema("extract-listing").Should().Be(PostExtractionDefinitions.ListingSchema);
        PostExtractionDefinitions.OutputSchema("post-tags").Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldAcceptAFullListingAnswer()
    {
        var parsed = PostExtractionDefinitions.Parse<ExtractListingAnswer>(FullListing)!;

        parsed.Should().NotBeNull();
        parsed.Flags.Should().BeEmpty();
        parsed.PropertyKind.Should().Be(ListingKind.Apartment);
        parsed.OfferType.Should().Be(ListingOfferType.RentOrSale);
        parsed.Price!.Currency.Should().Be(ListingCurrency.Usd);
        parsed.Item.AreaM2.Should().Be(new ListingAreas(54.5m, 30m, 9m));
        parsed.Item.Renovation.Should().Be(ListingRenovationLevel.NeedsRenovation);
        parsed.Author.Kind.Should().Be(ListingAuthorKind.ManagementCompany);
        parsed.ApartmentAmenities.Should().Equal(ListingApartmentAmenity.AirConditioner, ListingApartmentAmenity.WashingMachine);
        parsed.Landmarks.Should().ContainSingle().Which.Type.Should().Be(ListingLandmarkType.MetroStation);
        parsed.RentalTerms!.Commission.Status.Should().Be(RentalTermStatus.Present);
        parsed.RentalTerms.Commission.Claims.Single().Value!.Percent.Should().Be(50m);
        parsed.RentalTerms.AdvanceRent.Claims.Single().Value!.Duration!.Unit.Should().Be(ListingDurationUnit.Month);
        JsonSerializer.Deserialize<ExtractListingAnswer>(FullListing, EvidenceWire.Options).Should().NotBeNull("the strict read takes it as written");
    }

    [Theory]
    [InlineData("author", "phone", "\"+998 90 123 45 67\"")]
    [InlineData("author", "name", "\"Ali\"")]
    [InlineData("", "authorName", "\"Ali\"")]
    [InlineData("", "phoneNumbers", "[\"+998 90 123 45 67\"]")]
    [InlineData("item", "contact", "\"@someone\"")]
    public void Parse_ShouldRejectAContactField_StrictlyAndRemoveItWithAFlagThroughTheNormalizer(string owner, string property, string value)
    {
        var answer = JsonNode.Parse(FullListing)!.AsObject();
        (owner.Length == 0 ? answer : answer[owner]!.AsObject())[property] = JsonNode.Parse(value);
        string json = answer.ToJsonString();

        FluentActions.Invoking(() => JsonSerializer.Deserialize<ExtractListingAnswer>(json, EvidenceWire.Options))
            .Should().Throw<JsonException>("the strict read allows no property the schema lacks");
        var parsed = PostExtractionDefinitions.Parse<ExtractListingAnswer>(json)!;
        parsed.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag(owner.Length == 0 ? property : $"{owner}.{property}",
            AnswerNormalizer.UnknownField, null));
        PostExtractionValidation.ValidateListing(parsed, new ExtractionCorpus("Kvartira\nIjara", 15), new ExtractionTags("listing",
            [new PostTagsDomain("real-estate", true)], null, "evergreen", null)).Answer.GetRawText().Should().NotContain("+998").And.NotContain("@someone");
    }

    [Fact]
    public void Parse_ShouldRefuseAMissingField_AndNonJson()
    {
        PostExtractionDefinitions.Parse<ExtractListingAnswer>(FullListing.Replace("\"urgent\":false,", "")).Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractListingAnswer>(FullListing.Replace("\"flags\":[]", "\"x\":1")).Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractListingAnswer>("not json").Should().BeNull();
    }

    private static readonly string[] ContactWords = ["phone", "contact", "email", "mail", "telegram", "whatsapp", "viber", "handle", "registered", "realtorname", "authorname"];

    private const string FullListing = """
        {"title":"2-room flat","categoryPath":["Real estate","Rent"],"propertyKind":"apartment","invalid":null,"offerType":"rent-or-sale",
         "price":{"amount":500,"currency":"usd","period":"month","negotiable":true,"evidence":"Narxi 500 USD"},
         "item":{"propertyType":"secondary","buildingType":"brick","yearBuilt":1998,"rooms":2,"layout":"adjacent-separate",
                 "areaM2":{"total":54.5,"living":30,"kitchen":9},"floor":3,"floors":9,"ceilingM":2.7,"bathrooms":{"count":1,"type":"separate"},
                 "renovation":"needs-renovation","building":"Skyline Tower"},
         "furnished":"partly","apartmentAmenities":["air-conditioner","washing-machine"],"nearbyAmenities":["school","metro"],
         "location":{"district":"Chilanzar","city":"Tashkent","address":null},
         "landmarks":[{"type":"metro-station","name":"Oybek","evidence":"Oybek metro yonida"}],
         "author":{"kind":"management-company"},"noAgents":true,"relation":{"role":"announcement","changes":[],"status":null},
         "rentalTerms":{"deposit":{"status":"present","claims":[{"evidence":"Depozit: 500 USD","value":{"amount":500,"currency":"usd","rentMultiple":null}}]},
                        "advanceRent":{"status":"present","claims":[{"evidence":"1 oy oldindan","value":{"amount":null,"currency":null,"rentMultiple":null,"duration":{"count":1,"unit":"month"}}}]},
                        "utilities":{"status":"absent","claims":[{"evidence":"kommunal alohida","value":{"billing":"separate"}}]},
                        "minimumStay":{"status":"unknown","claims":[]},
                        "commission":{"status":"present","claims":[{"evidence":"Komissiya 50%","value":{"amount":null,"currency":null,"percent":50,"rentMultiple":null}}]}},
         "acceptedTenants":["family","students"],"excludedTenants":["animals","smoking"],"urgent":false,"flags":[]}
        """;

    private static void Walk(JsonNode? node, string path, List<string> names, List<string> open)
    {
        if (node is not JsonObject json)
            return;
        if (json["properties"] is JsonObject properties)
        {
            if (json["additionalProperties"] is not JsonValue closed || closed.GetValue<bool>())
                open.Add(path);
            foreach (var (name, child) in properties)
            {
                names.Add(name);
                Walk(child, $"{path}.{name}", names, open);
            }
        }
        if (json["items"] is { } items)
            Walk(items, path + "[]", names, open);
    }

    private static JsonObject Parse(string schema) => JsonNode.Parse(schema)!.AsObject();

    private static List<string?> Names(JsonNode? node) => node switch
    {
        JsonArray array => [.. array.Select(item => item?.GetValue<string>())],
        JsonValue value => [value.GetValue<string>()],
        _ => [],
    };
}
