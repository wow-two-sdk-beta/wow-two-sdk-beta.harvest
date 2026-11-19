using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>The repair before the strict read: a value the enum lacks becomes unsure plus a flag, a list drops it, case and
/// separators map, an unknown property goes with a flag, and a wrong JSON type still rejects the answer.</summary>
public sealed class AnswerNormalizerTests
{
    private const string Title = "Tashkent Fashion Week 2026";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Toshkentda bo'ladi. Kirish 50 000 so'm.";

    private static readonly ExtractionTags Fashion = new("event", [new PostTagsDomain("style-fashion", true)], null, "upcoming", null);
    private static readonly ExtractionTags Rental = new("listing", [new PostTagsDomain("real-estate", true)], null, "evergreen", null);

    [Fact]
    public void Parse_ShouldDropAnOutOfListItem_AndCarryItsFlagToTheStoredAnswer()
    {
        var json = EventJson(answer =>
        {
            answer["participation"] = new JsonArray("exhibitor", "designers");
            answer["fashion"]!["segments"] = new JsonArray("apparel", "speakers", "textile");
        });

        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(json)!;
        var validated = PostExtractionValidation.ValidateEvent(parsed, Corpus(), Fashion);

        parsed.Participation.Should().Equal(EventParticipation.Exhibitor);
        parsed.Fashion!.Segments.Should().Equal(FashionSegment.Apparel, FashionSegment.Textile);
        validated.Flags.Should().Contain(new PostTagsFlag("participation[1]", AnswerNormalizer.OutsideList, "designers"))
            .And.Contain(new PostTagsFlag("fashion.segments[1]", AnswerNormalizer.OutsideList, "speakers"));
        validated.Answer.GetProperty("flags").GetArrayLength().Should().Be(validated.Flags.Count);
        validated.Answer.GetProperty("participation").EnumerateArray().Select(value => value.GetString()).Should().Equal("exhibitor");
    }

    [Fact]
    public void Parse_ShouldTurnAnOutOfListSingleValueUnsure_WithOneFlag()
    {
        var json = EventJson(answer =>
        {
            answer["relation"]!["role"] = "news";
            answer["fashion"]!["audience"] = "everyone";
        });

        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(json)!;
        var validated = PostExtractionValidation.ValidateEvent(parsed, Corpus(), Fashion);

        parsed.Relation.Role.Should().Be(RelationRole.Unsure);
        parsed.Fashion!.Audience.Should().Be(FashionAudience.Unsure);
        validated.Flags.Should().BeEquivalentTo(
        [
            new PostTagsFlag("relation.role", AnswerNormalizer.OutsideList, "news"),
            new PostTagsFlag("fashion.audience", AnswerNormalizer.OutsideList, "everyone"),
        ]);
        validated.Role.Should().Be("unsure");
    }

    [Fact]
    public void Parse_ShouldFlagAListingAuthorOnce_WhenItsKindIsOutsideTheList()
    {
        var json = ListingJson(answer => answer["author"]!["kind"] = "broker");

        var validated = PostExtractionValidation.ValidateListing(PostExtractionDefinitions.Parse<ExtractListingAnswer>(json)!, Corpus(), Rental);

        validated.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("author.kind", AnswerNormalizer.OutsideList, "broker"));
        validated.Answer.GetProperty("author").GetProperty("kind").GetString().Should().Be("unsure");
    }

    [Fact]
    public void Parse_ShouldUseUnknown_WhereTheEnumHasNoUnsure()
    {
        var access = PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer => answer["access"] = "members"))!;
        var term = PostExtractionDefinitions.Parse<ExtractListingAnswer>(ListingJson(answer =>
            answer["rentalTerms"]!["deposit"]!["status"] = "maybe"))!;

        access.Access.Should().Be(EventAccess.Unknown);
        access.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("access", AnswerNormalizer.OutsideList, "members"));
        term.RentalTerms!.Deposit.Status.Should().Be(RentalTermStatus.Unknown);
        term.Flags.Should().ContainSingle().Which.Field.Should().Be("rentalTerms.deposit.status");
    }

    [Fact]
    public void Parse_ShouldClearANullableValueWithNoFallback_AndRejectARequiredOne()
    {
        var cleared = PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer =>
        {
            answer["relation"] = new JsonObject { ["role"] = "update", ["changes"] = new JsonArray("dates"), ["status"] = "closed" };
        }))!;
        var listing = PostExtractionDefinitions.Parse<ExtractListingAnswer>(ListingJson(answer =>
            answer["price"] = new JsonObject { ["amount"] = 500, ["currency"] = "USD", ["period"] = "fortnight", ["evidence"] = "500 USD" }))!;
        var required = EventJson(answer => answer["occurrences"]![0]!["precision"] = "date");

        cleared.Relation.Status.Should().BeNull();
        cleared.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("relation.status", AnswerNormalizer.OutsideList, "closed"));
        listing.Price!.Period.Should().BeNull();
        listing.Flags.Should().ContainSingle().Which.Field.Should().Be("price.period");
        PostExtractionDefinitions.Parse<ExtractEventAnswer>(required).Should().BeNull();
    }

    [Theory]
    [InlineData("Trade Fair")]
    [InlineData("TRADE_FAIR")]
    [InlineData("trade-fair")]
    [InlineData("tradeFair")]
    [InlineData("  trade  fair ")]
    public void Parse_ShouldMapCaseAndSeparatorVariantsWithoutAFlag(string format)
    {
        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer => answer["format"] = format))!;

        parsed.Format.Should().Be(EventFormat.TradeFair);
        parsed.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldMapVariantsInNestedValuesAndLists_AndRemoveDuplicates()
    {
        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer =>
        {
            answer["participation"] = new JsonArray("EXHIBITOR", "Visitor", "exhibitor");
            answer["relation"] = new JsonObject { ["role"] = "UPDATE", ["changes"] = new JsonArray("Dates"), ["status"] = "Sold_Out" };
        }))!;

        parsed.Participation.Should().Equal(EventParticipation.Exhibitor, EventParticipation.Visitor);
        parsed.Relation.Role.Should().Be(RelationRole.Update);
        parsed.Relation.Changes.Should().Equal(RelationChange.Dates);
        parsed.Relation.Status.Should().Be(RelationStatus.SoldOut);
        parsed.Flags.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldRemoveAnUnknownProperty_WithAFlag()
    {
        var json = EventJson(answer =>
        {
            answer["notes"] = "some remark";
            answer["place"]!["region"] = "Tashkent region";
            answer["occurrences"]![0]!["confidence"] = 0.9;
        });

        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(json)!;

        parsed.Flags.Should().BeEquivalentTo(
        [
            new PostTagsFlag("notes", AnswerNormalizer.UnknownField, null),
            new PostTagsFlag("place.region", AnswerNormalizer.UnknownField, null),
            new PostTagsFlag("occurrences[0].confidence", AnswerNormalizer.UnknownField, null),
        ]);
        parsed.Occurrences.Should().ContainSingle();
    }

    [Fact]
    public void Parse_ShouldKeepTheModelsOwnFlagsFirst()
    {
        var json = EventJson(answer =>
        {
            answer["flags"] = new JsonArray(new JsonObject { ["field"] = "organizer", ["reason"] = "not stated", ["proposed"] = null });
            answer["extra"] = 1;
        });

        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(json)!;

        parsed.Flags.Should().Equal(new PostTagsFlag("organizer", "not stated", null), new PostTagsFlag("extra", AnswerNormalizer.UnknownField, null));
    }

    [Fact]
    public void Parse_ShouldCreateTheFlagsList_WhenTheModelSentNull()
    {
        var json = EventJson(answer =>
        {
            answer["flags"] = null;
            answer["participation"] = new JsonArray("designers");
        });

        PostExtractionDefinitions.Parse<ExtractEventAnswer>(json)!.Flags.Should().ContainSingle()
            .Which.Should().Be(new PostTagsFlag("participation[0]", AnswerNormalizer.OutsideList, "designers"));
    }

    [Fact]
    public void Parse_ShouldClipTheProposedValue_AndKeepNoContact()
    {
        string stray = new('x', 80);
        var parsed = PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer =>
            answer["participation"] = new JsonArray(stray, "call +998 90 123 45 67", "  ", "@some_handle")))!;

        parsed.Flags.Select(flag => flag.Proposed).Should().Equal(new string('x', AnswerNormalizer.MaxProposed), null, null, null);
        parsed.Flags.Select(flag => flag.Field).Should().Equal("participation[0]", "participation[1]", "participation[2]", "participation[3]");
    }

    [Theory]
    [InlineData("name", "\"Fashion Week\"")]
    [InlineData("place", "\"Tashkent\"")]
    [InlineData("organizer", "{\"name\":\"x\"}")]
    [InlineData("format", "5")]
    [InlineData("format", "null")]
    [InlineData("access", "[\"free\"]")]
    [InlineData("participation", "\"exhibitor\"")]
    [InlineData("participation", "[\"exhibitor\",3]")]
    [InlineData("prices", "{}")]
    [InlineData("editionLabel", "{\"year\":2026}")]
    public void Parse_ShouldStillRejectAWrongJsonType(string property, string value)
    {
        var json = EventJson(answer => answer[property] = JsonNode.Parse(value));

        PostExtractionDefinitions.Parse<ExtractEventAnswer>(json).Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldStillRejectAMissingFieldAndNonJson()
    {
        PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer => answer.Remove("status"))).Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractEventAnswer>(EventJson(answer =>
        {
            answer.Remove("flags");
            answer["participation"] = new JsonArray("designers");
        })).Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractEventAnswer>("not json").Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractEventAnswer>("[]").Should().BeNull();
        PostExtractionDefinitions.Parse<ExtractEventAnswer>("{\"a\":1,\"a\":2}").Should().BeNull();
    }

    [Fact]
    public void Normalize_ShouldReturnAValidAnswerUnchanged_AndStoreTheSameOutput()
    {
        string eventJson = EventJson(_ => { });
        string listingJson = ListingJson(_ => { });

        PostExtractionDefinitions.Normalizer.Normalize(eventJson, typeof(ExtractEventAnswer)).Should().BeSameAs(eventJson);
        PostExtractionDefinitions.Normalizer.Normalize(listingJson, typeof(ExtractListingAnswer)).Should().BeSameAs(listingJson);

        var strictEvent = JsonSerializer.Deserialize<ExtractEventAnswer>(eventJson, EvidenceWire.Options)!;
        var strictListing = JsonSerializer.Deserialize<ExtractListingAnswer>(listingJson, EvidenceWire.Options)!;
        PostExtractionValidation.ValidateEvent(PostExtractionDefinitions.Parse<ExtractEventAnswer>(eventJson)!, Corpus(), Fashion)
            .Answer.GetRawText().Should().Be(PostExtractionValidation.ValidateEvent(strictEvent, Corpus(), Fashion).Answer.GetRawText());
        PostExtractionValidation.ValidateListing(PostExtractionDefinitions.Parse<ExtractListingAnswer>(listingJson)!, Corpus(), Rental)
            .Answer.GetRawText().Should().Be(PostExtractionValidation.ValidateListing(strictListing, Corpus(), Rental).Answer.GetRawText());
    }

    [Fact]
    public void Parse_ShouldRepairATagsAnswer()
    {
        string tags = JsonSerializer.Serialize(new
        {
            entityType = "job", domains = new[] { new { node = "real-estate", primary = true } }, topics = new[] { "rent" },
            about = "a flat", timeframe = "Evergreen", items = 1, language = "ru", confidence = 0.8, titleEn = "Flat",
            flags = Array.Empty<object>(), extra = 1,
        });

        var parsed = PostTagsDefinition.Parse(tags)!;

        parsed.EntityType.Should().Be(PostEntityType.Unsure);
        parsed.Timeframe.Should().Be(PostTimeframe.Evergreen);
        parsed.Flags.Should().BeEquivalentTo(
        [
            new PostTagsFlag("entityType", AnswerNormalizer.OutsideList, "job"),
            new PostTagsFlag("extra", AnswerNormalizer.UnknownField, null),
        ]);
        PostTagsValidation.Validate(parsed, new HashSet<string> { "real-estate" }).Flags.Count(flag => flag.Field == "entityType").Should().Be(1);
    }

    private static string EventJson(Action<JsonObject> change)
    {
        var answer = JsonNode.Parse("""
            {"name":{"en":"Tashkent Fashion Week","original":null},"editionLabel":"2026",
             "occurrences":[{"starts":"2026-12-12","ends":"2026-12-14","precision":"day","certainty":"stated","evidence":"12-14 dekabr"}],
             "place":{"venue":null,"city":"Tashkent","country":"Uzbekistan"},"organizer":null,"format":"trade-fair","access":"free",
             "prices":[{"amount":50000,"currency":"UZS","label":"entry","evidence":"Kirish 50 000 so'm"}],
             "registration":{"opens":null,"deadline":null,"link":null},"participation":["exhibitor","visitor"],"status":"scheduled",
             "relation":{"role":"announcement","changes":[],"status":null},
             "fashion":{"segments":["apparel"],"garmentCategories":["dresses"],"exhibitorProfile":["designers"],"audience":"trade"},
             "flags":[]}
            """)!.AsObject();
        change(answer);
        return answer.ToJsonString();
    }

    private static string ListingJson(Action<JsonObject> change)
    {
        var answer = JsonNode.Parse("""
            {"title":"2-room flat","offerType":"rent","categoryPath":["Real estate","Rent"],"price":null,
             "location":{"district":"Chilanzar","city":"Tashkent","address":null},
             "item":{"rooms":2,"areaM2":54,"floor":3,"floors":9,"building":null,"brand":null,"model":null,"year":null},
             "author":{"kind":"owner"},"relation":{"role":"announcement","changes":[],"status":null},
             "rentalTerms":{"deposit":{"status":"unknown","claims":[]},"minimumStay":{"status":"unknown","claims":[]},
                            "advanceRent":{"status":"unknown","claims":[]},"utilities":{"status":"unknown","claims":[]}},
             "flags":[]}
            """)!.AsObject();
        change(answer);
        return answer.ToJsonString();
    }

    private static ExtractionCorpus Corpus() =>
        PostExtractionContent.Corpus(new PostEnvelope
        {
            Title = Title, Text = Text, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        }, 6000);
}
