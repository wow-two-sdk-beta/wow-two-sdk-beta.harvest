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
        var listing = PostExtractionDefinitions.Parse<ExtractListingAnswer>(ListingJson(answer => answer["invalid"] = "spam"))!;
        var required = EventJson(answer => answer["occurrences"]![0]!["precision"] = "date");

        cleared.Relation.Status.Should().BeNull();
        cleared.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("relation.status", AnswerNormalizer.OutsideList, "closed"));
        listing.Invalid.Should().BeNull("a nullable pick with no unsure clears");
        listing.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("invalid", AnswerNormalizer.OutsideList, "spam"));
        PostExtractionDefinitions.Parse<ExtractEventAnswer>(required).Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldRepairOutOfListVersionTwoValues_ToUnsureWithAFlagEach()
    {
        var parsed = PostExtractionDefinitions.Parse<ExtractListingAnswer>(ListingJson(answer =>
        {
            answer["propertyKind"] = "villa";
            answer["offerType"] = "lease";
            answer["furnished"] = "somewhat";
            answer["price"] = JsonNode.Parse("""{"amount":500,"currency":"eur","period":"fortnight","negotiable":false,"evidence":"500 EUR"}""");
            var item = answer["item"]!;
            item["layout"] = "loft";
            item["renovation"] = "Euro Renovation";
            item["bathrooms"]!["type"] = "outdoor";
            answer["author"]!["kind"] = "broker";
            answer["landmarks"] = JsonNode.Parse("""[{"type":"bus stop","name":"Minor","evidence":"Minor yonida"}]""");
        }))!;

        parsed.PropertyKind.Should().Be(ListingKind.Unsure);
        parsed.OfferType.Should().Be(ListingOfferType.Unsure);
        parsed.Furnished.Should().Be(ListingFurnished.Unsure);
        parsed.Price!.Currency.Should().Be(ListingCurrency.Unsure);
        parsed.Price.Period.Should().Be(ListingAskPeriod.Unsure);
        parsed.Item.Layout.Should().Be(ListingRoomLayout.Unsure);
        parsed.Item.Renovation.Should().Be(ListingRenovationLevel.Unsure);
        parsed.Item.Bathrooms.Type.Should().Be(ListingBathroomType.Unsure);
        parsed.Author.Kind.Should().Be(ListingAuthorKind.Unsure);
        parsed.Landmarks.Single().Type.Should().Be(ListingLandmarkType.Unsure);
        parsed.Flags.Should().BeEquivalentTo(
        [
            new PostTagsFlag("propertyKind", AnswerNormalizer.OutsideList, "villa"),
            new PostTagsFlag("offerType", AnswerNormalizer.OutsideList, "lease"),
            new PostTagsFlag("furnished", AnswerNormalizer.OutsideList, "somewhat"),
            new PostTagsFlag("price.currency", AnswerNormalizer.OutsideList, "eur"),
            new PostTagsFlag("price.period", AnswerNormalizer.OutsideList, "fortnight"),
            new PostTagsFlag("item.layout", AnswerNormalizer.OutsideList, "loft"),
            new PostTagsFlag("item.renovation", AnswerNormalizer.OutsideList, "Euro Renovation"),
            new PostTagsFlag("item.bathrooms.type", AnswerNormalizer.OutsideList, "outdoor"),
            new PostTagsFlag("author.kind", AnswerNormalizer.OutsideList, "broker"),
            new PostTagsFlag("landmarks[0].type", AnswerNormalizer.OutsideList, "bus stop"),
        ]);
        var validated = PostExtractionValidation.ValidateListing(parsed, Corpus(), Rental);
        validated.Flags.Count(flag => flag.Field is "offerType" or "propertyKind" or "furnished").Should().Be(3);
        validated.Flags.Count(flag => flag.Field == "offerType").Should().Be(1, "the normalizer's flag stands for the unsure pick");
    }

    [Fact]
    public void Parse_ShouldRepairVersionTwoLists_DropAnOutOfListItemAndMapVariantsSilently()
    {
        var parsed = PostExtractionDefinitions.Parse<ExtractListingAnswer>(ListingJson(answer =>
        {
            answer["apartmentAmenities"] = new JsonArray("balcony", "wifi", "Air Conditioner", "WASHING_MACHINE", "balcony");
            answer["nearbyAmenities"] = new JsonArray("Metro", "zoo");
            answer["acceptedTenants"] = new JsonArray("Family", "pets", "Students");
            answer["excludedTenants"] = new JsonArray("animals", "smoking", "animals");
        }))!;

        parsed.ApartmentAmenities.Should().Equal(ListingApartmentAmenity.Balcony, ListingApartmentAmenity.AirConditioner, ListingApartmentAmenity.WashingMachine);
        parsed.NearbyAmenities.Should().Equal(ListingNearbyAmenity.Metro);
        parsed.AcceptedTenants.Should().Equal(ListingTenant.Family, ListingTenant.Students);
        parsed.ExcludedTenants.Should().Equal(ListingTenant.Animals, ListingTenant.Smoking);
        parsed.Flags.Should().BeEquivalentTo(
        [
            new PostTagsFlag("apartmentAmenities[1]", AnswerNormalizer.OutsideList, "wifi"),
            new PostTagsFlag("nearbyAmenities[1]", AnswerNormalizer.OutsideList, "zoo"),
            new PostTagsFlag("acceptedTenants[1]", AnswerNormalizer.OutsideList, "pets"),
        ]);
    }

    [Fact]
    public void Parse_ShouldRepairVersionTwoClaimValues_InsideRentalTerms()
    {
        var parsed = PostExtractionDefinitions.Parse<ExtractListingAnswer>(ListingJson(answer =>
        {
            var terms = answer["rentalTerms"]!;
            terms["deposit"] = JsonNode.Parse("""{"status":"present","claims":[{"evidence":"Depozit 500 USD","value":{"amount":500,"currency":"US Dollar","rentMultiple":null}}]}""");
            terms["minimumStay"] = JsonNode.Parse("""{"status":"present","claims":[{"evidence":"3 oy","value":{"count":3,"unit":"months"}}]}""");
            terms["utilities"] = JsonNode.Parse("""{"status":"present","claims":[{"evidence":"kommunal","value":{"billing":"mixed"}}]}""");
            terms["commission"] = JsonNode.Parse("""{"status":"present","claims":[{"evidence":"komissiya 50%","value":{"amount":null,"currency":null,"percent":50,"rentMultiple":null}}]}""");
        }))!;

        var terms = parsed.RentalTerms!;
        terms.Deposit.Claims.Single().Value!.Currency.Should().Be(ListingCurrency.Unsure);
        terms.MinimumStay.Claims.Single().Value!.Unit.Should().Be(ListingDurationUnit.Unsure);
        terms.Utilities.Claims.Single().Value!.Billing.Should().Be(ListingUtilitiesBilling.Unsure);
        terms.Commission.Claims.Single().Value!.Percent.Should().Be(50m);
        parsed.Flags.Select(flag => flag.Field).Should().BeEquivalentTo("rentalTerms.deposit.claims[0].value.currency",
            "rentalTerms.minimumStay.claims[0].value.unit", "rentalTerms.utilities.claims[0].value.billing");
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
            {"title":"2-room flat","categoryPath":["Real estate","Rent"],"propertyKind":"apartment","invalid":null,"offerType":"rent","price":null,
             "item":{"propertyType":null,"buildingType":null,"yearBuilt":null,"rooms":2,"layout":null,
                     "areaM2":{"total":54,"living":null,"kitchen":null},"floor":3,"floors":9,"ceilingM":null,
                     "bathrooms":{"count":null,"type":null},"renovation":null,"building":null},
             "furnished":"unknown","apartmentAmenities":[],"nearbyAmenities":[],
             "location":{"district":"Chilanzar","city":"Tashkent","address":null},"landmarks":[],
             "author":{"kind":"owner"},"noAgents":false,"relation":{"role":"announcement","changes":[],"status":null},
             "rentalTerms":{"deposit":{"status":"unknown","claims":[]},"advanceRent":{"status":"unknown","claims":[]},
                            "utilities":{"status":"unknown","claims":[]},"minimumStay":{"status":"unknown","claims":[]},
                            "commission":{"status":"unknown","claims":[]}},
             "acceptedTenants":[],"excludedTenants":[],"urgent":false,"flags":[]}
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
