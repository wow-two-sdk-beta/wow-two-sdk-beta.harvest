using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>The evidence check and the field rules: a quote outside the post turns its value unknown with a flag, the
/// rest of the answer stays; blocks fill only for their domain; contacts never stay.</summary>
public sealed class PostExtractionValidationTests
{
    private const string Title = "Tashkent Fashion Week 2026";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Toshkentda bo'ladi. Kirish 50 000 so'm. Ro'yxatdan o'tish 1 dekabrgacha.";

    private static readonly ExtractionTags Fashion = new("event", [new PostTagsDomain("style-fashion/womens-fashion", true)], null, "upcoming", null);
    private static readonly ExtractionTags Business = new("event", [new PostTagsDomain("business-and-finance", true),
        new PostTagsDomain("style-fashion", false)], null, "upcoming", null);
    private static readonly ExtractionTags Rental = new("listing", [new PostTagsDomain("real-estate", true)], null, "evergreen", null);

    [Fact]
    public void ValidateEvent_ShouldKeepQuotedValues_AndTurnAnUnquotedOneUnknownWithAFlag()
    {
        var answer = Event() with
        {
            Occurrences =
            [
                new("2026-12-12", "2026-12-14", DatePrecision.Day, DateCertainty.Stated, "12-14 dekabr"),
                new("2027-03-01", null, DatePrecision.Day, DateCertainty.Stated, "1 mart"),
            ],
            Prices = [new(50_000m, "uzs", "entry", "Kirish 50 000 so'm"), new(70_000m, "UZS", "vip", "Kirish 50 000 so'm")],
            Registration = new(null, new EvidencedDate("2026-12-01", DatePrecision.Day, "1 dekabrgacha"), "https://fw.example/apply"),
        };

        var validated = PostExtractionValidation.ValidateEvent(answer, Corpus(), Fashion);
        var root = validated.Answer;

        root.GetProperty("occurrences").GetArrayLength().Should().Be(1);
        root.GetProperty("occurrences")[0].GetProperty("evidence").GetString().Should().Be("12-14 dekabr");
        root.GetProperty("prices").GetArrayLength().Should().Be(1);
        root.GetProperty("prices")[0].GetProperty("currency").GetString().Should().Be("UZS");
        root.GetProperty("registration").GetProperty("deadline").GetProperty("value").GetString().Should().Be("2026-12-01");
        validated.Flags.Should().Contain(new PostTagsFlag("occurrences", PostExtractionValidation.QuoteNotFound, null))
            .And.Contain(new PostTagsFlag("prices", PostExtractionValidation.AmountNotQuoted, "70000"));
        root.GetProperty("flags").GetArrayLength().Should().Be(validated.Flags.Count);
        validated.Role.Should().Be("announcement");
    }

    [Theory]
    [InlineData("haftaligi 12")]
    [InlineData("aftaligi 12-14")]
    [InlineData("12-14 dekabr kunlari Toshkentda bo'ladi.")]
    public void ValidateEvent_ShouldCheckQuotesOnWholeTokens(string quote)
    {
        var answer = Event() with { Occurrences = [new("2026-12-12", null, DatePrecision.Day, DateCertainty.Stated, quote)] };

        var occurrences = PostExtractionValidation.ValidateEvent(answer, Corpus(), Fashion).Answer.GetProperty("occurrences");

        occurrences.GetArrayLength().Should().Be(quote.StartsWith("aftaligi", StringComparison.Ordinal) ? 0 : 1);
    }

    [Fact]
    public void ValidateEvent_ShouldRefuseAQuoteBeyondWhatTheModelRead()
    {
        var answer = Event() with { Occurrences = [new("2026-12-01", null, DatePrecision.Day, DateCertainty.Stated, "1 dekabrgacha")] };

        var validated = PostExtractionValidation.ValidateEvent(answer, Corpus(maxTextChars: 60), Fashion);

        validated.Answer.GetProperty("occurrences").GetArrayLength().Should().Be(0);
        validated.Flags.Should().ContainSingle(flag => flag.Field == "occurrences");
    }

    [Theory]
    [InlineData("2026", DatePrecision.Year, true)]
    [InlineData("2026-12", DatePrecision.Month, true)]
    [InlineData("2026-12-12", DatePrecision.Day, true)]
    [InlineData("2026-12-12T19:00", DatePrecision.Minute, true)]
    [InlineData("2026-12", DatePrecision.Day, false)]
    [InlineData("12.12.2026", DatePrecision.Day, false)]
    [InlineData("2026-13-01", DatePrecision.Day, false)]
    public void IsDate_ShouldMatchThePrecision(string value, DatePrecision precision, bool expected)
    {
        PostExtractionValidation.IsDate(value, precision).Should().Be(expected);
    }

    [Fact]
    public void ValidateEvent_ShouldKeepTheFashionBlock_OnlyUnderAPrimaryStyleFashionDomain()
    {
        var answer = Event() with
        {
            Fashion = new([FashionSegment.Apparel, FashionSegment.Apparel, FashionSegment.Unsure], ["Evening Dresses", "атлас"],
                [FashionExhibitor.Designers], FashionAudience.Trade),
        };

        var fashion = PostExtractionValidation.ValidateEvent(answer, Corpus(), Fashion);
        var elsewhere = PostExtractionValidation.ValidateEvent(answer, Corpus(), Business);

        var block = fashion.Answer.GetProperty("fashion");
        block.GetProperty("segments").EnumerateArray().Select(value => value.GetString()).Should().Equal("apparel", "unsure");
        block.GetProperty("garmentCategories").EnumerateArray().Select(value => value.GetString()).Should().Equal("evening-dresses");
        fashion.Flags.Should().Contain(new PostTagsFlag("fashion.segments", PostExtractionValidation.ChoseUnsure, null))
            .And.Contain(new PostTagsFlag("fashion.garmentCategories", PostTagsValidation.NotEnglish, "атлас"));
        elsewhere.Answer.GetProperty("fashion").ValueKind.Should().Be(JsonValueKind.Null, "the primary domain is business-and-finance");
    }

    [Fact]
    public void Validate_ShouldClearChangesOutsideAnUpdate_AndAddTheStatusChange()
    {
        var mention = PostExtractionValidation.ValidateEvent(Event() with
        {
            Relation = new(RelationRole.Mention, [RelationChange.Dates], RelationStatus.Cancelled),
        }, Corpus(), Fashion).Answer.GetProperty("relation");
        var update = PostExtractionValidation.ValidateEvent(Event() with
        {
            Relation = new(RelationRole.Update, [RelationChange.Dates], RelationStatus.Postponed),
        }, Corpus(), Fashion);

        mention.GetProperty("changes").GetArrayLength().Should().Be(0);
        mention.GetProperty("status").ValueKind.Should().Be(JsonValueKind.Null);
        update.Answer.GetProperty("relation").GetProperty("changes").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("dates", "status");
        update.Role.Should().Be("update");
    }

    [Fact]
    public void Validate_ShouldRemoveContacts_FromFreeText()
    {
        var validated = PostExtractionValidation.ValidateEvent(Event() with
        {
            Organizer = "Call +998 90 123 45 67", Place = new("Expo hall, info@fw.uz", "Tashkent", "Uzbekistan"),
        }, Corpus(), Fashion);

        validated.Answer.GetProperty("organizer").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("place").GetProperty("venue").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("place").GetProperty("city").GetString().Should().Be("Tashkent");
        validated.Flags.Where(flag => flag.Field == "contacts").Select(flag => flag.Proposed).Should().BeEquivalentTo("organizer", "place");
        PostExtractionValidation.HasContact("Visa Fashion Week 2026").Should().BeFalse();
        PostExtractionValidation.HasContact("t.me/fashionweek").Should().BeTrue();
    }

    [Fact]
    public void ValidateListing_ShouldCheckEachRentalTermOnItsOwn_AndKeepTheOthers()
    {
        const string text = "Сдается 2-комнатная квартира. Цена 500 USD в месяц. Депозит: 500 USD. Коммунальные включены.";
        var corpus = new ExtractionCorpus($"Квартира\n{text}", $"Квартира\n{text}".Length);
        var answer = Listing() with
        {
            Price = new ListingAsk(500m, "usd", ListingAskPeriod.Month, "Цена 500 USD в месяц"),
            RentalTerms = RentalTermsAnswer.Empty with
            {
                Deposit = new(RentalTermStatus.Present, [new("Депозит: 500 USD", new RentalDeposit(600m, "USD", null))]),
                Utilities = new(RentalTermStatus.Present, [new("Коммунальные включены", new RentalUtilities(RentalUtilitiesBilling.Included))]),
            },
        };

        var validated = PostExtractionValidation.ValidateListing(answer, corpus, Rental);
        var notRent = PostExtractionValidation.ValidateListing(answer with { OfferType = ListingOfferType.Sale }, corpus, Rental);

        var terms = validated.Answer.GetProperty("rentalTerms");
        terms.GetProperty("deposit").GetProperty("status").GetString().Should().Be("unknown");
        terms.GetProperty("deposit").GetProperty("claims").GetArrayLength().Should().Be(0);
        terms.GetProperty("utilities").GetProperty("status").GetString().Should().Be("present");
        validated.Answer.GetProperty("price").GetProperty("currency").GetString().Should().Be("USD");
        validated.Flags.Should().ContainSingle(flag => flag.Field == "rentalTerms.deposit");
        notRent.Answer.GetProperty("rentalTerms").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ValidateListing_ShouldDropAnUnquotedPrice_AndFlagUnsurePicks()
    {
        var corpus = new ExtractionCorpus("Toyota Camry\nSotiladi, narxi kelishiladi.", 40);
        var answer = Listing() with
        {
            OfferType = ListingOfferType.Unsure, Author = new(ListingAuthorKind.Unsure),
            Price = new ListingAsk(25_000m, "USD", null, "narxi 25 000$"),
            Item = new(2, -5m, 400, null, null, "Toyota", "Camry", 2019),
        };

        var validated = PostExtractionValidation.ValidateListing(answer, corpus, Rental);

        validated.Answer.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("areaM2").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("floor").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("model").GetString().Should().Be("Camry");
        validated.Flags.Select(flag => flag.Field).Should().Contain(["offerType", "author", "price"]);
    }

    [Theory]
    [InlineData("Депозит 500 тыс. сум", 500_000, true)]
    [InlineData("Narxi 1,5 mln so'm", 1_500_000, true)]
    [InlineData("50 000 so'm", 50_000, true)]
    [InlineData("50 000 so'm", 5_000, false)]
    [InlineData("Kirish bepul", 0, false)]
    public void StatesAmount_ShouldReadScaledNumbers(string quote, decimal amount, bool expected)
    {
        RentalTermEvidence.StatesAmount(quote, amount).Should().Be(expected);
    }

    private static ExtractionCorpus Corpus(int maxTextChars = 6000) =>
        PostExtractionContent.Corpus(new PostEnvelope
        {
            Title = Title, Text = Text, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        }, maxTextChars);

    private static ExtractEventAnswer Event() => new(new EventName("Tashkent Fashion Week", "Toshkent moda haftaligi"), "2026", [],
        new EventPlace(null, "Tashkent", "Uzbekistan"), null, EventFormat.Runway, EventAccess.Ticketed, [],
        new EventRegistration(null, null, null), [EventParticipation.Visitor], EventStatus.Scheduled,
        new EntityRelation(RelationRole.Announcement, [], null), null, []);

    private static ExtractListingAnswer Listing() => new("2-room flat", ListingOfferType.Rent, ["Недвижимость", "Аренда"], null,
        new ListingLocation("Chilanzar", "Tashkent", null), new ListingItem(2, 54m, 3, 9, null, null, null, null),
        new ListingAuthor(ListingAuthorKind.Owner), new EntityRelation(RelationRole.Announcement, [], null), null, []);
}
