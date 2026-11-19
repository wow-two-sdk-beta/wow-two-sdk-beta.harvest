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
    public void Score_ShouldKeepACommonVehicleWeak_UnlessTheSameAuthorOffersIt()
    {
        var car = new ListingFacts { OfferType = "sale", City = "Tashkent", Brand = "Chevrolet", Model = "Cobalt", Year = 2021,
            Prices = [new ListingPrice(11_500m, "USD", null)] };
        var ad = car with { Prices = [new ListingPrice(11_000m, "USD", null)] };

        ListingIdentity.Score(ad, car, false, Defaults).Should().BeLessThan(Defaults.MinMatchConfidence, "many such cars are for sale");
        ListingIdentity.Score(ad, car, true, Defaults).Should().BeGreaterThanOrEqualTo(Defaults.MinMatchConfidence);
        ListingIdentity.Score(car with { Year = 2019 }, car, true, Defaults).Should().Be(0);
    }
}
