using System.Text.Json.Nodes;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace Harvest.Tests.Unit.Entities;

/// <summary>Listing identity scoring: the same flat across authors and scripts scores high; a known fact that differs
/// rules the pair out.</summary>
public sealed class ListingIdentityTests
{
    private static readonly EntityOptions Defaults = new();

    private static readonly ListingFacts Flat = new()
    {
        OfferType = "rent", CategoryPath = ["Недвижимость", "Квартиры", "Аренда"], City = "Tashkent", District = "Yunusobod tumani", Rooms = 2,
        AreaM2 = 64m, Floor = 5, Floors = 9, Prices = [new ListingPrice(500m, "USD", "month")],
    };

    [Fact]
    public void Score_ShouldMatchTheSameFlat_FromAnotherAuthorInAnotherScript()
    {
        var ad = Flat with
        {
            City = "Ташкент", District = "Юнусабадский район", AreaM2 = 62m, CategoryPath = ["Недвижимость", "Квартиры"],
            Prices = [new ListingPrice(550m, "USD", "month")],
        };

        ListingIdentity.Score(ad, Flat, sameAuthor: false, Defaults).Should().BeGreaterThanOrEqualTo(Defaults.MinMatchConfidence);
    }

    [Theory]
    [InlineData("offer")]
    [InlineData("area")]
    [InlineData("rooms")]
    [InlineData("district")]
    [InlineData("city")]
    [InlineData("floor")]
    [InlineData("category")]
    public void Score_ShouldRuleOut_AKnownFactThatDiffers(string fact)
    {
        var ad = fact switch
        {
            "offer" => Flat with { OfferType = "sale" },
            "area" => Flat with { AreaM2 = 68m },
            "rooms" => Flat with { Rooms = 3 },
            "district" => Flat with { District = "Chilonzor" },
            "city" => Flat with { City = "Samarkand" },
            "floor" => Flat with { Floor = 6 },
            _ => Flat with { CategoryPath = ["Недвижимость", "Дома"] },
        };

        ListingIdentity.Score(ad, Flat, sameAuthor: true, Defaults).Should().Be(0);
    }

    [Fact]
    public void Score_ShouldStayWeak_WithoutTheFactsThatIdentifyAFlat()
    {
        var thin = new ListingFacts { OfferType = "rent", City = "Tashkent", Rooms = 2 };

        ListingIdentity.Score(thin, Flat, sameAuthor: false, Defaults).Should().BeLessThan(Defaults.MinMatchConfidence);
        ListingIdentity.Score(thin, Flat, sameAuthor: true, Defaults).Should().BeGreaterThan(ListingIdentity.Score(thin, Flat, false, Defaults));
    }

    [Fact]
    public void Score_ShouldLowerAPriceOutsideTheBand_AndIgnoreAnotherCurrency()
    {
        double inside = ListingIdentity.Score(Flat, Flat, false, Defaults);
        double outside = ListingIdentity.Score(Flat with { Prices = [new ListingPrice(900m, "USD", "month")] }, Flat, false, Defaults);
        double other = ListingIdentity.Score(Flat with { Prices = [new ListingPrice(6_000_000m, "UZS", "month")] }, Flat, false, Defaults);

        outside.Should().BeLessThan(inside - 0.2);
        other.Should().BeLessThan(inside).And.BeGreaterThan(outside);
        ListingIdentity.Score(Flat with { Prices = [new ListingPrice(900m, "USD", "month")] }, Flat, false,
            new EntityOptions { ListingPriceBand = 1 }).Should().Be(inside);
    }

    [Fact]
    public void Score_ShouldFollowTheConfiguredAreaTolerance()
    {
        var ad = Flat with { AreaM2 = 68m };

        ListingIdentity.Score(ad, Flat, false, new EntityOptions { ListingAreaTolerance = 0.1 }).Should().BeGreaterThan(0.75);
    }

    [Fact]
    public void Score_ShouldKeepAThinListingWeak_UnlessTheSameAuthorOffersIt()
    {
        var thin = new ListingFacts { OfferType = "sale", City = "Tashkent", Rooms = 2, Building = "Sample Residence", YearBuilt = 2021,
            Prices = [new ListingPrice(60_000m, "USD", null)] };
        var ad = thin with { Prices = [new ListingPrice(58_000m, "USD", null)] };

        ListingIdentity.Score(ad, thin, false, Defaults).Should().BeLessThan(Defaults.MinMatchConfidence, "many such flats are for sale");
        ListingIdentity.Score(ad, thin, true, Defaults).Should().BeGreaterThanOrEqualTo(Defaults.MinMatchConfidence);
    }

    [Fact]
    public void Score_ShouldRuleOut_ADifferentYearBuiltOrBuilding()
    {
        var known = Flat with { Building = "Sample Residence", YearBuilt = 2021 };

        ListingIdentity.Score(known with { YearBuilt = 2019 }, known, true, Defaults).Should().Be(0);
        ListingIdentity.Score(known with { Building = "Other Residence" }, known, true, Defaults).Should().Be(0);
        ListingIdentity.Score(known with { YearBuilt = null, Building = null }, known, true, Defaults).Should().BeGreaterThan(0);
    }

    [Fact]
    public void ListingFactsOf_ShouldReadTheItemPathsTheAnswerWrites_AndNoBrandOrModel()
    {
        var facts = (JsonObject)JsonNode.Parse("""
            {"offerType":"rent","categoryPath":["Недвижимость"],"location":{"city":"Tashkent","district":"Yunusobod"},
             "item":{"rooms":2,"areaM2":64.5,"floor":5,"floors":9,"building":"Sample Residence","yearBuilt":2021,"brand":"X","model":"Y","year":1999},
             "priceSpread":[{"currency":"USD","period":"month","min":500,"max":550}]}
            """)!;

        var read = EntityResolver.ListingFactsOf(facts);

        (read.OfferType, read.City, read.District, read.Rooms, read.AreaM2, read.Floor, read.Floors, read.Building, read.YearBuilt)
            .Should().Be(("rent", "Tashkent", "Yunusobod", 2, 64.5m, 5, 9, "Sample Residence", 2021));
        read.CategoryPath.Should().Equal("Недвижимость");
        read.Prices.Select(price => (price.Amount, price.Currency, price.Period)).Should().Equal((500m, "USD", "month"), (550m, "USD", "month"));
    }
}
