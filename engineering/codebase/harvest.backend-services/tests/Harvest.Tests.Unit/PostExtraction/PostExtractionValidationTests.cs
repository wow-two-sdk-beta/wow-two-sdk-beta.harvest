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

    [Theory]
    [InlineData("Fashion Week 2026, 12.12.2026", "Fashion Week 2026, 12.12.2026")]
    [InlineData("Call +998 90 123 45 67 today", "Call … today")]
    [InlineData("Write info@fw.uz or @fashion_uz", "Write … or …")]
    [InlineData("Join t.me/fashionweek now", "Join … now")]
    [InlineData("Дизайнеры, звоните 998901234567", "Дизайнеры, звоните …")]
    public void Scrub_ShouldReplaceContacts_AndKeepTheRest(string title, string expected)
    {
        PostExtractionValidation.Scrub(title).Should().Be(expected);
        PostExtractionValidation.HasContact(PostExtractionValidation.Scrub(title)).Should().BeFalse();
    }

    [Fact]
    public void ValidateListing_ShouldCheckEachRentalTermOnItsOwn_AndKeepTheOthers()
    {
        const string text = "Сдается 2-комнатная квартира. Цена 500 USD в месяц. Депозит: 500 USD. Коммунальные включены.";
        var corpus = new ExtractionCorpus($"Квартира\n{text}", $"Квартира\n{text}".Length);
        var answer = Listing() with
        {
            Price = Ask(500m, "Цена 500 USD в месяц"),
            RentalTerms = ListingRentalTerms.Empty with
            {
                Deposit = new(RentalTermStatus.Present, [new("Депозит: 500 USD", new ListingDeposit(600m, ListingCurrency.Usd, null))]),
                Utilities = new(RentalTermStatus.Present, [new("Коммунальные включены", new ListingUtilities(ListingUtilitiesBilling.Included))]),
            },
        };

        var validated = PostExtractionValidation.ValidateListing(answer, corpus, Rental);
        var notRent = PostExtractionValidation.ValidateListing(answer with { OfferType = ListingOfferType.Sale }, corpus, Rental);

        var terms = validated.Answer.GetProperty("rentalTerms");
        terms.GetProperty("deposit").GetProperty("status").GetString().Should().Be("unknown");
        terms.GetProperty("deposit").GetProperty("claims").GetArrayLength().Should().Be(0);
        terms.GetProperty("utilities").GetProperty("status").GetString().Should().Be("present");
        terms.GetProperty("commission").GetProperty("status").GetString().Should().Be("unknown");
        validated.Answer.GetProperty("price").GetProperty("currency").GetString().Should().Be("usd");
        validated.Flags.Should().ContainSingle(flag => flag.Field == "rentalTerms.deposit");
        notRent.Answer.GetProperty("rentalTerms").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void ValidateListing_ShouldDropAnUnquotedPrice_AndFlagUnsurePicks()
    {
        var corpus = new ExtractionCorpus("Toyota Camry\nSotiladi, narxi kelishiladi.", 40);
        var answer = Listing() with
        {
            OfferType = ListingOfferType.Unsure, Author = new(ListingAuthorKind.Unsure), Furnished = ListingFurnished.Unsure,
            Price = Ask(25_000m, "narxi 25 000$") with { Currency = ListingCurrency.Unsure },
            Item = Listing().Item with
            {
                Rooms = 2, AreaM2 = new(-5m, null, null), Floor = 400, Floors = 9, Layout = ListingRoomLayout.Unsure, Building = "Skyline Tower",
                Bathrooms = new(1, ListingBathroomType.Unsure), YearBuilt = 2019,
            },
            ApartmentAmenities = [ListingApartmentAmenity.Balcony, ListingApartmentAmenity.Balcony, ListingApartmentAmenity.Unsure],
            AcceptedTenants = [ListingTenant.Family, ListingTenant.Unsure],
        };

        var validated = PostExtractionValidation.ValidateListing(answer, corpus, Rental);

        validated.Answer.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("areaM2").GetProperty("total").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("floor").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("building").GetString().Should().Be("Skyline Tower");
        validated.Answer.GetProperty("apartmentAmenities").EnumerateArray().Select(value => value.GetString()).Should().Equal("balcony", "unsure");
        validated.Flags.Select(flag => flag.Field).Should().Contain(["offerType", "author", "furnished", "item.layout", "item.bathrooms.type",
            "apartmentAmenities", "acceptedTenants", "price"]);
    }

    [Fact]
    public void ValidateListing_ShouldFlagAnUnsureCurrencyOnAKeptPrice_AndAnAreaAboveTheTotal()
    {
        const string text = "Arenda kvartira. Narxi 500 dollar.";
        var corpus = new ExtractionCorpus($"Kvartira\n{text}", $"Kvartira\n{text}".Length);
        var answer = Listing() with
        {
            Price = new ListingAsk(500m, ListingCurrency.Unsure, ListingAskPeriod.Unsure, false, "Narxi 500 dollar"),
            Item = Listing().Item with { AreaM2 = new(40m, 55m, 10m), Floor = 12, Floors = 9 },
        };

        var validated = PostExtractionValidation.ValidateListing(answer, corpus, Rental);

        validated.Answer.GetProperty("price").GetProperty("currency").GetString().Should().Be("unsure");
        validated.Answer.GetProperty("item").GetProperty("areaM2").GetProperty("living").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Answer.GetProperty("item").GetProperty("areaM2").GetProperty("kitchen").GetDecimal().Should().Be(10m);
        validated.Flags.Should().Contain(new PostTagsFlag("price.currency", PostExtractionValidation.ChoseUnsure, null))
            .And.Contain(new PostTagsFlag("price.period", PostExtractionValidation.ChoseUnsure, null))
            .And.Contain(new PostTagsFlag("item.areaM2.living", "an area above the total", "55"))
            .And.Contain(new PostTagsFlag("item.floor", "the floor is above the building's floors", "12"));
    }

    [Fact]
    public void ValidateListing_ShouldKeepALandmarkOnlyWithItsQuote_AndNeverAContact()
    {
        const string text = "Kvartira Oybek metro yonida. Qo'ng'iroq: Ali, 90 123 45 67.";
        var corpus = Corpus(new PostEnvelope { Title = "Kvartira", Text = text, Extraction = Stamp() });
        var answer = Listing() with
        {
            Landmarks =
            [
                new(ListingLandmarkType.MetroStation, "Oybek", "Oybek metro yonida"),
                new(ListingLandmarkType.MetroStation, "Oybek", "Oybek metro yonida"),
                new(ListingLandmarkType.Supermarket, "Korzinka", "Korzinka yonida"),
                new(ListingLandmarkType.Other, "+998 90 123 45 67", "Oybek metro yonida"),
            ],
        };

        var validated = PostExtractionValidation.ValidateListing(answer, corpus, Rental);

        validated.Answer.GetProperty("landmarks").EnumerateArray().Select(value => value.GetProperty("name").GetString()).Should().Equal("Oybek");
        validated.Flags.Should().Contain(new PostTagsFlag("landmarks", PostExtractionValidation.QuoteNotFound, null))
            .And.Contain(new PostTagsFlag("contacts", PostExtractionValidation.ContactRemoved, "landmarks"));
        corpus.Text.Should().Contain("[phone]").And.NotContain("123 45 67");
    }

    [Fact]
    public void ValidateListing_ShouldBackAnAbsentCommission_WithAQuoteCopiedFromTheAttributes()
    {
        var corpus = Corpus(new PostEnvelope
        {
            Title = "Kvartira", Text = "Сдается 2-комнатная. Цена 500 USD в месяц.", Extraction = Stamp(),
            Extras = Olx(attributes: new Dictionary<string, string> { ["Комиссионные"] = "Нет", ["Количество комнат"] = "2" }),
        });
        ListingRentalTerms Terms(string quote) => ListingRentalTerms.Empty with
        {
            Commission = new(RentalTermStatus.Absent, [new(quote, null)]),
        };

        var fromAttributes = PostExtractionValidation.ValidateListing(Listing() with { RentalTerms = Terms("Комиссионные: Нет") }, corpus, Rental);
        var elsewhere = PostExtractionValidation.ValidateListing(Listing() with { RentalTerms = Terms("Комиссионные: Да") }, corpus, Rental);
        var spanning = PostExtractionValidation.ValidateListing(Listing() with { RentalTerms = Terms("в месяц. Комиссионные: Нет") }, corpus, Rental);

        corpus.Attributes.Should().Be("Комиссионные: Нет\nКоличество комнат: 2");
        var commission = fromAttributes.Answer.GetProperty("rentalTerms").GetProperty("commission");
        commission.GetProperty("status").GetString().Should().Be("absent");
        commission.GetProperty("claims")[0].GetProperty("evidence").GetString().Should().Be("Комиссионные: Нет");
        fromAttributes.Flags.Should().NotContain(flag => flag.Field.StartsWith("rentalTerms", StringComparison.Ordinal));
        elsewhere.Answer.GetProperty("rentalTerms").GetProperty("commission").GetProperty("status").GetString().Should().Be("unknown");
        elsewhere.Flags.Should().ContainSingle(flag => flag.Field == "rentalTerms.commission");
        spanning.Answer.GetProperty("rentalTerms").GetProperty("commission").GetProperty("status").GetString().Should().Be("unknown");
    }

    [Fact]
    public void ValidateListing_ShouldAcceptAnAttributeLineAsAPriceQuote_AndStillCheckItsAmount()
    {
        var corpus = Corpus(new PostEnvelope
        {
            Title = "Kvartira", Text = "Arenda.", Extraction = Stamp(),
            Extras = Olx(attributes: new Dictionary<string, string> { ["Цена"] = "650 y.e." }),
        });

        var kept = PostExtractionValidation.ValidateListing(Listing() with { Price = Ask(650m, "Цена: 650 y.e.") }, corpus, Rental);
        var wrong = PostExtractionValidation.ValidateListing(Listing() with { Price = Ask(700m, "Цена: 650 y.e.") }, corpus, Rental);

        kept.Answer.GetProperty("price").GetProperty("evidence").GetString().Should().Be("Цена: 650 y.e.");
        wrong.Answer.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Комиссия 50% от месячной аренды", 50, true)]
    [InlineData("Комиссия 50% от месячной аренды", 5, false)]
    [InlineData("Агентская комиссия 0,5 месячной аренды", 50, false)]
    [InlineData("Komissiya 30 foiz", 30, true)]
    [InlineData("Комиссия 10 процентов", 10, true)]
    public void CheckListingTerms_ShouldReadAPercentCommission(string quote, int percent, bool kept)
    {
        var terms = ListingRentalTerms.Empty with
        {
            Commission = new(RentalTermStatus.Present, [new(quote, new ListingCommission(null, null, percent, null))]),
        };

        var (checkedTerms, rejected) = RentalTermEvidence.CheckListingTerms(terms, quote, quote.Length);

        checkedTerms.Commission.Status.Should().Be(kept ? RentalTermStatus.Present : RentalTermStatus.Unknown);
        rejected.Should().Equal(kept ? Array.Empty<string>() : new[] { "commission" });
    }

    [Theory]
    [InlineData("Без комиссии", true)]
    [InlineData("Комиссия 0%", true)]
    [InlineData("Комиссионные: Нет", true)]
    [InlineData("Комиссия не взимается", true)]
    [InlineData("Komissiya yo'q", true)]
    [InlineData("Комиссия 50%", false)]
    public void CheckListingTerms_ShouldReadAbsentCommissionWordings(string quote, bool kept)
    {
        var terms = ListingRentalTerms.Empty with { Commission = new(RentalTermStatus.Absent, [new(quote, null)]) };

        RentalTermEvidence.CheckListingTerms(terms, quote, quote.Length).Answer.Commission.Status
            .Should().Be(kept ? RentalTermStatus.Absent : RentalTermStatus.Unknown);
    }

    [Fact]
    public void CheckListingTerms_ShouldReadAmountsInLowercaseCurrencies_AndRefuseAnUnreadableUnit()
    {
        const string text = "Депозит: 500 USD. Минимум 3 месяца. Предоплата 1 месяц.";
        var terms = ListingRentalTerms.Empty with
        {
            Deposit = new(RentalTermStatus.Present, [new("Депозит: 500 USD", new ListingDeposit(500m, ListingCurrency.Usd, null))]),
            MinimumStay = new(RentalTermStatus.Present, [new("Минимум 3 месяца", new ListingDuration(3m, ListingDurationUnit.Month))]),
            AdvanceRent = new(RentalTermStatus.Present, [new("Предоплата 1 месяц", new ListingAdvance(null, null, null, new ListingDuration(1m, ListingDurationUnit.Hour)))]),
        };

        var (kept, rejected) = RentalTermEvidence.CheckListingTerms(terms, text, text.Length);

        kept.Deposit.Status.Should().Be(RentalTermStatus.Present);
        kept.MinimumStay.Status.Should().Be(RentalTermStatus.Present);
        kept.AdvanceRent.Status.Should().Be(RentalTermStatus.Unknown, "an hour unit no quote can back");
        rejected.Should().Equal("advanceRent");
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

    [Theory]
    [InlineData("Narxi 800 mingdan", 800_000, true)]
    [InlineData("Narxi 800 mingga", 800_000, true)]
    [InlineData("800 minglik kvartira", 800_000, true)]
    [InlineData("800ming-dan", 800_000, true)]
    [InlineData("2,5 mlndan boshlab", 2_500_000, true)]
    [InlineData("3 millionga", 3_000_000, true)]
    [InlineData("2 mlrdgacha", 2_000_000_000, true)]
    [InlineData("1,5 milliardni", 1_500_000_000, true)]
    [InlineData("800 мингдан", 800_000, true)]
    [InlineData("5 млндан", 5_000_000, true)]
    [InlineData("2 млрд", 2_000_000_000, true)]
    [InlineData("800 mingle", 800_000, false)]
    [InlineData("800 mingdani", 800_000, false)]
    [InlineData("5 mlnx", 5_000_000, false)]
    [InlineData("5 kdan", 5_000, false)]
    [InlineData("800 mingdan", 800_000_000, false)]
    public void StatesAmount_ShouldReadUzbekSuffixedAndBillionScaleWords_AndRefuseWordsThatOnlyStartLikeThem(string quote, decimal amount, bool expected)
    {
        RentalTermEvidence.StatesAmount(quote, amount).Should().Be(expected);
    }

    [Fact]
    public void ValidateListing_ShouldKeepAPrice_WhoseQuoteTakesAnUzbekSuffixedScaleWord()
    {
        const string text = "Arenda kvartira. Narxi 800 mingdan.";
        var corpus = new ExtractionCorpus($"Kvartira\n{text}", $"Kvartira\n{text}".Length);

        var validated = PostExtractionValidation.ValidateListing(
            Listing() with { Price = Ask(800_000m, "Narxi 800 mingdan", ListingCurrency.Uzs) }, corpus, Rental);

        validated.Answer.GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(800_000m);
        validated.Flags.Should().NotContain(flag => flag.Field == "price");
    }

    [Theory]
    [InlineData("Депозит: 1 800y.e.")]
    [InlineData("Депозит: 1 800 у.е.")]
    [InlineData("Депозит 1800 у. е.")]
    [InlineData("Depozit: 1 800 y.e.")]
    [InlineData("Депозит 1800 уе")]
    public void CheckTerms_ShouldReadConventionalUnitsAsUsd(string text)
    {
        var answer = RentalTermsAnswer.Empty with
        {
            Deposit = new(RentalTermStatus.Present, [new(text, new RentalDeposit(1800m, "USD", null))]),
        };

        var (kept, rejected) = RentalTermEvidence.CheckTerms(answer, text, text.Length);

        rejected.Should().BeEmpty();
        kept.Deposit.Status.Should().Be(RentalTermStatus.Present);
    }

    [Theory]
    [InlineData("UZS")]
    [InlineData(null)]
    public void CheckTerms_ShouldRefuseAConventionalUnitClaimedAsAnotherCurrency(string? currency)
    {
        const string text = "Депозит: 1 800y.e.";
        var answer = RentalTermsAnswer.Empty with
        {
            Deposit = new(RentalTermStatus.Present, [new(text, new RentalDeposit(1800m, currency, null))]),
        };

        RentalTermEvidence.CheckTerms(answer, text, text.Length).Rejected.Should().Equal("deposit");
    }

    [Fact]
    public void CheckTerms_ShouldRefuseAPresentDeposit_WhoseConventionalUnitAmountIsWaived()
    {
        const string text = "Депозит 500 у.е. не нужен";
        var answer = RentalTermsAnswer.Empty with
        {
            Deposit = new(RentalTermStatus.Present, [new("Депозит 500 у.е.", new RentalDeposit(500m, "USD", null))]),
        };

        RentalTermEvidence.CheckTerms(answer, text, text.Length).Rejected.Should().Equal("deposit");
    }

    [Theory]
    [InlineData("Komunal to'lovlar ichida")]
    [InlineData("Kamunal to'lovlar ichida")]
    [InlineData("Kommunal to'lovlar ichida")]
    [InlineData("Комуналка включена")]
    [InlineData("Коммуналка включена")]
    public void CheckTerms_ShouldReadEveryUtilitiesSpelling(string text)
    {
        var answer = RentalTermsAnswer.Empty with
        {
            Utilities = new(RentalTermStatus.Present, [new(text, new RentalUtilities(RentalUtilitiesBilling.Included))]),
        };

        RentalTermEvidence.CheckTerms(answer, text, text.Length).Rejected.Should().BeEmpty();
    }

    [Fact]
    public void CheckTerms_ShouldRefuseAUtilitiesQuote_WhoseWordOnlyStartsLikeTheCue()
    {
        const string text = "Komunikatsiya ichida";
        var answer = RentalTermsAnswer.Empty with
        {
            Utilities = new(RentalTermStatus.Present, [new(text, new RentalUtilities(RentalUtilitiesBilling.Included))]),
        };

        RentalTermEvidence.CheckTerms(answer, text, text.Length).Rejected.Should().Equal("utilities");
    }

    [Fact]
    public void ValidateListing_ShouldKeepAPriceQuotedOnlyFromThePricesLine_AndStillCheckItsAmount()
    {
        var corpus = Corpus(new PostEnvelope
        {
            Title = "Kvartira", Text = "Arenda, narx kelishiladi.", Extraction = Stamp(),
            Prices = [new PostPrice { Amount = 800_000m, Currency = "UZS", Per = "month" }],
        });
        var kept = PostExtractionValidation.ValidateListing(Listing() with { Price = Ask(800_000m, "listed: 800000 UZS/month", ListingCurrency.Uzs) }, corpus, Rental);
        var wrongAmount = PostExtractionValidation.ValidateListing(Listing() with { Price = Ask(900_000m, "listed: 800000 UZS/month", ListingCurrency.Uzs) }, corpus, Rental);
        var elsewhere = PostExtractionValidation.ValidateListing(Listing() with { Price = Ask(800_000m, "narxi 800 ming", ListingCurrency.Uzs) }, corpus, Rental);

        kept.Answer.GetProperty("price").GetProperty("evidence").GetString().Should().Be("listed: 800000 UZS/month");
        wrongAmount.Answer.GetProperty("price").ValueKind.Should().Be(JsonValueKind.Null);
        wrongAmount.Flags.Should().Contain(new PostTagsFlag("price", PostExtractionValidation.AmountNotQuoted, "900000"));
        elsewhere.Flags.Should().Contain(new PostTagsFlag("price", PostExtractionValidation.QuoteNotFound, null));
    }

    [Fact]
    public void Corpus_ShouldHoldTheRendersThePromptReceives_AndLeaveOutEstimatesAndAbsentOnes()
    {
        var envelope = new PostEnvelope
        {
            Title = Title, Text = Text, Extraction = Stamp(),
            Dates =
            [
                new PostDate { Role = DateRole.Start, Value = "2026-12-12", Precision = DatePrecision.Day },
                new PostDate { Role = DateRole.End, Value = "2027-02-02", Precision = DatePrecision.Day, Certainty = DateCertainty.Estimated },
            ],
            Prices = [new PostPrice { Role = PriceRole.Ticket, Amount = 50_000m, Currency = "UZS" }],
            Location = new PostLocation { Mentions = [new LocationMention { Text = "Tashkent", Kind = LocationKind.City }] },
        };

        var corpus = Corpus(envelope);
        var bare = Corpus(new PostEnvelope { Title = Title, Text = Text, Extraction = Stamp() });

        corpus.Dates.Should().Be("start: 2026-12-12");
        corpus.Prices.Should().Be("ticket: 50000 UZS");
        corpus.Place.Should().Be("Tashkent");
        corpus.Dates.Should().Be(PostExtractionContent.Variables(envelope, null, Fashion, 6000)["dates"]);
        bare.Structured.Should().BeEmpty("the renders read none and unknown");
    }

    [Fact]
    public void ValidateEvent_ShouldKeepADateQuotedFromTheDatesLine_AndRefuseAnEstimateQuotedFromIt()
    {
        var corpus = Corpus(new PostEnvelope
        {
            Title = Title, Text = Text, Extraction = Stamp(),
            Dates =
            [
                new PostDate { Role = DateRole.Start, Value = "2026-12-12", Precision = DatePrecision.Day },
                new PostDate { Role = DateRole.End, Value = "2027-02-02", Precision = DatePrecision.Day, Certainty = DateCertainty.Estimated },
            ],
        });
        var answer = Event() with
        {
            Occurrences =
            [
                new("2026-12-12", null, DatePrecision.Day, DateCertainty.Stated, "start: 2026-12-12"),
                new("2027-02-02", null, DatePrecision.Day, DateCertainty.Stated, "end: 2027-02-02"),
            ],
        };

        var validated = PostExtractionValidation.ValidateEvent(answer, corpus, Fashion);

        validated.Answer.GetProperty("occurrences").EnumerateArray().Select(value => value.GetProperty("starts").GetString())
            .Should().Equal("2026-12-12");
        validated.Flags.Should().ContainSingle(flag => flag.Field == "occurrences").Which.Reason.Should().Be(PostExtractionValidation.QuoteNotFound);
    }

    [Theory]
    [InlineData("12-14 dekabr", "2026-12-12", DatePrecision.Day, true)]
    [InlineData("1 dekabrgacha", "2026-12-01", DatePrecision.Day, true)]
    [InlineData("dekabrda", "2026-12", DatePrecision.Month, true)]
    [InlineData("17−18 ноября", "2026-11-17", DatePrecision.Day, true)]
    [InlineData("с декабря", "2026-12", DatePrecision.Month, true)]
    [InlineData("December 12th", "2026-12-12", DatePrecision.Day, true)]
    [InlineData("Dec 5", "2026-12-12", DatePrecision.Day, true)]
    [InlineData("2026-12-12", "2026-12-12", DatePrecision.Day, true)]
    [InlineData("12.12.2026 19:00", "2026-12-12T19:00", DatePrecision.Minute, true)]
    [InlineData("Tashkent Fashion Week 2026", "2026", DatePrecision.Year, true)]
    [InlineData("Tashkent Fashion Week 2026", "2026-12-12", DatePrecision.Day, false)]
    [InlineData("Tashkent Fashion Week 2026", "2026-12", DatePrecision.Month, false)]
    [InlineData("Moda haftaligi", "2026", DatePrecision.Year, false)]
    [InlineData("this Saturday", "2026-12-12", DatePrecision.Day, false)]
    [InlineData("yanvarda", "2026-12-12", DatePrecision.Day, false)]
    [InlineData("12-14 dekabr", "2026-11-20", DatePrecision.Day, false)]
    [InlineData("dekabrda", "2026-12-12", DatePrecision.Minute, true)]
    [InlineData("kun maydonida", "2026-12", DatePrecision.Month, false)]
    public void StatesDate_ShouldNeedADayOrMonthNumberOrNameThatMatches(string quote, string value, DatePrecision precision, bool expected)
    {
        RentalTermEvidence.StatesDate(quote, value, precision).Should().Be(expected);
    }

    [Fact]
    public void ValidateEvent_ShouldDropAStatedDate_WhoseQuoteStatesNoDate()
    {
        var answer = Event() with
        {
            Occurrences = [new("2026-12-12", null, DatePrecision.Day, DateCertainty.Stated, "Tashkent Fashion Week 2026")],
            Registration = new(null, new EvidencedDate("2026-12-01", DatePrecision.Day, "Moda haftaligi"), null),
        };

        var validated = PostExtractionValidation.ValidateEvent(answer, Corpus(), Fashion);

        validated.Answer.GetProperty("occurrences").GetArrayLength().Should().Be(0);
        validated.Answer.GetProperty("registration").GetProperty("deadline").ValueKind.Should().Be(JsonValueKind.Null);
        validated.Flags.Should().Contain(new PostTagsFlag("occurrences", PostExtractionValidation.DateNotQuoted, "2026-12-12"))
            .And.Contain(new PostTagsFlag("registration.deadline", PostExtractionValidation.DateNotQuoted, "2026-12-01"));
    }

    [Fact]
    public void ValidateEvent_ShouldKeepAnEstimatedDate_WhoseQuoteIsOnlyCheckedToExist()
    {
        var answer = Event() with
        {
            Occurrences = [new("2026-12-12", null, DatePrecision.Day, DateCertainty.Estimated, "Moda haftaligi")],
        };

        var validated = PostExtractionValidation.ValidateEvent(answer, Corpus(), Fashion);

        validated.Answer.GetProperty("occurrences").GetArrayLength().Should().Be(1);
        validated.Flags.Should().BeEmpty();
    }

    [Fact]
    public void ValidateEvent_ShouldKeepAStatedDateTheSourceDatesHold_WhateverItsQuote()
    {
        var corpus = Corpus(new PostEnvelope
        {
            Title = Title, Text = Text, Extraction = Stamp(),
            Dates = [new PostDate { Role = DateRole.Start, Value = "2026-12-12", Precision = DatePrecision.Day, End = "2026-12-14" }],
        });
        var answer = Event() with
        {
            Occurrences =
            [
                new("2026-12-12", "2026-12-14", DatePrecision.Day, DateCertainty.Stated, Title),
                new("2026-12-13", null, DatePrecision.Day, DateCertainty.Stated, Title),
            ],
        };

        var validated = PostExtractionValidation.ValidateEvent(answer, corpus, Fashion);

        validated.Answer.GetProperty("occurrences").EnumerateArray().Select(value => value.GetProperty("starts").GetString())
            .Should().Equal("2026-12-12");
        validated.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("occurrences", PostExtractionValidation.DateNotQuoted, "2026-12-13"));
    }

    private static ExtractionCorpus Corpus(int maxTextChars = 6000) =>
        PostExtractionContent.Corpus(new PostEnvelope
        {
            Title = Title, Text = Text, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        }, maxTextChars);

    private static ExtractionCorpus Corpus(PostEnvelope envelope) => PostExtractionContent.Corpus(envelope, 6000);

    private static ExtractionStamp Stamp() => new() { NormVersion = "test@1", RulesVersion = "test@1" };

    private static ExtractEventAnswer Event() => new(new EventName("Tashkent Fashion Week", "Toshkent moda haftaligi"), "2026", [],
        new EventPlace(null, "Tashkent", "Uzbekistan"), null, EventFormat.Runway, EventAccess.Ticketed, [],
        new EventRegistration(null, null, null), [EventParticipation.Visitor], EventStatus.Scheduled,
        new EntityRelation(RelationRole.Announcement, [], null), null, []);

    private static ListingAsk Ask(decimal amount, string quote, ListingCurrency currency = ListingCurrency.Usd) =>
        new(amount, currency, ListingAskPeriod.Month, false, quote);

    /// <summary>Builds the <c>extra</c> map of an OLX ad whose source fields carry the given attributes.</summary>
    private static Dictionary<string, JsonElement> Olx(Dictionary<string, string> attributes, bool? isBusiness = null) => new()
    {
        ["source"] = JsonSerializer.SerializeToElement(new
        {
            schema = "olx.ad.v1", parserVersion = "olx-html@2", fields = new Dictionary<string, object?>
            {
                ["externalId"] = "ID0", ["title"] = "Kvartira", ["attributes"] = attributes, ["isBusiness"] = isBusiness,
            },
        }),
    };

    private static ExtractListingAnswer Listing() => new("2-room flat", ["Недвижимость", "Аренда"], ListingKind.Apartment, null, ListingOfferType.Rent,
        null, new ListingItem(null, null, null, 2, null, new ListingAreas(54m, null, null), 3, 9, null, new ListingBathrooms(null, null), null, null),
        ListingFurnished.Unknown, [], [], new ListingLocation("Chilanzar", "Tashkent", null), [], new ListingAuthor(ListingAuthorKind.Owner), false,
        new EntityRelation(RelationRole.Announcement, [], null), null, [], [], false, []);
}
