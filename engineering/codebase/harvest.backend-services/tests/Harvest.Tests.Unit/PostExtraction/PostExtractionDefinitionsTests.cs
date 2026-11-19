using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

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
    public void ListingSchema_ShouldKeepNoContacts_AndEmbedTheRentalTermsOfVersionTwo()
    {
        var schema = Parse(PostExtractionDefinitions.ListingSchema);
        var properties = schema["properties"]!;

        Names(schema["required"]).Should().BeEquivalentTo("title", "offerType", "categoryPath", "price", "location", "item", "author",
            "relation", "rentalTerms", "flags");
        Names(properties["offerType"]!["enum"]).Should().Equal("sale", "rent", "service", "unsure");
        Names(properties["author"]!["required"]).Should().Equal("kind");
        properties["author"]!["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        Names(properties["author"]!["properties"]!["kind"]!["enum"]).Should().Equal("owner", "agent", "business", "unknown", "unsure");
        Names(properties["price"]!["required"]).Should().BeEquivalentTo("amount", "currency", "period", "evidence");
        Names(properties["location"]!["required"]).Should().BeEquivalentTo("district", "city", "address");

        var terms = properties["rentalTerms"]!.DeepClone().AsObject();
        Names(terms["type"]).Should().Contain("null");
        var version2 = EvidenceWire.Options.GetJsonSchemaAsNode(typeof(RentalTermsAnswer),
            new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true }).AsObject();
        JsonNode.DeepEquals(terms["properties"], version2["properties"]).Should().BeTrue();
        Names(terms["required"]).Should().BeEquivalentTo("deposit", "minimumStay", "advanceRent", "utilities");
    }

    [Fact]
    public void Variables_ShouldCarryThePostFieldsAndItsTags()
    {
        PostExtractionDefinitions.Variables.Should().Contain(["title", "text", "dates", "place", "prices", "postedAt", "entityType", "domains",
            "about", "timeframe", "titleEn"]);
        PostExtractionDefinitions.OutputSchema("extract-event").Should().Be(PostExtractionDefinitions.EventSchema);
        PostExtractionDefinitions.OutputSchema("extract-listing").Should().Be(PostExtractionDefinitions.ListingSchema);
        PostExtractionDefinitions.OutputSchema("post-tags").Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldRefuseAnUnknownProperty_OrAMissingField()
    {
        var valid = JsonSerializer.Serialize(new
        {
            title = "2-room flat", offerType = "rent", categoryPath = new[] { "Недвижимость", "Аренда" }, price = (object?)null,
            location = new { district = (string?)null, city = "Tashkent", address = (string?)null },
            item = new { rooms = 2, areaM2 = (decimal?)null, floor = (int?)null, floors = (int?)null, building = (string?)null,
                brand = (string?)null, model = (string?)null, year = (int?)null },
            author = new { kind = "owner" }, relation = new { role = "announcement", changes = Array.Empty<string>(), status = (string?)null },
            rentalTerms = (object?)null, flags = Array.Empty<object>(),
        });

        PostExtractionDefinitions.Parse<ExtractListingAnswer>(valid).Should().NotBeNull();
        PostExtractionDefinitions.Parse<ExtractListingAnswer>(valid.Replace("\"author\":{\"kind\":\"owner\"}",
            "\"author\":{\"kind\":\"owner\",\"phone\":\"+998\"}")).Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractListingAnswer>(valid.Replace(",\"flags\":[]", "")).Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractListingAnswer>("not json").Should().BeNull();
    }

    private static JsonObject Parse(string schema) => JsonNode.Parse(schema)!.AsObject();

    private static List<string?> Names(JsonNode? node) => node switch
    {
        JsonArray array => [.. array.Select(item => item?.GetValue<string>())],
        JsonValue value => [value.GetValue<string>()],
        _ => [],
    };
}
