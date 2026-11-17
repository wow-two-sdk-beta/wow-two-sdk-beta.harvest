using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

namespace Harvest.Tests.Unit.Extraction;

public sealed class ListingSchemaMapperTests
{
    [Fact]
    public void MapsOlxFloorsAreasAndUnambiguousEnumsIndependently()
    {
        var projection = Map("olx.ad.v1", new
        {
            categoryPath = "/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/",
            priceText = "1 234,56 у.е.",
            attributes = new Dictionary<string, string>
            {
                ["Общая площадь"] = "98,25 м²",
                ["Количество комнат"] = "3",
                ["Этаж"] = "5",
                ["Этажность дома"] = "9",
                ["Тип строения"] = "Кирпичный",
                ["Планировка"] = "Смежно-раздельная",
                ["Тип жилья"] = "Вторичный рынок",
                ["Год постройки/сдачи"] = "1990 – 2000",
                ["Высота потолков"] = "280",
                ["Санузел"] = "2 санузла и более",
                ["Комиссионные"] = "Нет",
                ["Рядом есть"] = "Парк, зелёная зона, Школа",
            },
        });

        projection.Property.Kind.Should().Be(ListingPropertyKind.Apartment);
        projection.Property.MarketSegment.Should().Be(ListingMarketSegment.Resale);
        projection.Property.Construction.Should().Be(ListingConstruction.Brick);
        projection.Property.BuildYear.Should().Be(new ListingBuildYear(1990, 2000, ListingBuildYearRole.BuiltOrHandover));
        projection.Unit.TotalAreaM2.Should().Be(98.25m);
        projection.Unit.RoomConnectivity.Should().Be(ListingRoomConnectivity.Mixed);
        projection.Unit.Floor.Should().Be(5);
        projection.Unit.BuildingFloors.Should().Be(9);
        projection.Unit.CeilingHeightM.Should().Be(2.8m);
        projection.Unit.BathroomsMin.Should().Be(2);
        projection.Unit.BathroomArrangement.Should().BeNull();

        var offer = projection.Offers.Should().ContainSingle().Which;
        offer.Transaction.Should().Be(ListingTransaction.Rent);
        offer.Period.Should().BeNull();
        offer.Price!.Amount.Should().Be(1234.56m);
        offer.Price.Currency.Should().BeNull();
        offer.HasCommission.Should().BeFalse();
        projection.UnknownValues.Should().Contain(item => item.Reason == "unresolvedCurrency");
        projection.Features.Select(item => item.Code).Should().BeEquivalentTo(["park", "school"]);
        projection.Evidence.Should().Contain(item => item.Path == "/unit/ceilingHeightM" && item.Method == "unitConvention");
        projection.QualityFlags.Should().BeEmpty();
    }

    [Fact]
    public void PreservesConflictingAndImplausibleNativeFields()
    {
        var projection = Map("olx.ad.v1", new
        {
            priceText = "900 USD",
            price = new { regularPrice = new { value = 800, currencyCode = "USD" } },
            attributes = new Dictionary<string, string>
            {
                ["Этаж"] = "12",
                ["Этажность дома"] = "9",
                ["Общая площадь"] = "50",
                ["Жилая площадь"] = "60",
                ["Высота потолков"] = "2 850",
            },
        });
        projection.Offers[0].Price!.Amount.Should().Be(800);
        projection.Offers[0].Transaction.Should().BeNull();
        projection.Unit.CeilingHeightM.Should().BeNull();
        projection.QualityFlags.Should().BeEquivalentTo(["floorExceedsBuildingFloors", "livingAreaExceedsTotalArea", "priceAmountConflict"]);
        projection.UnknownValues.Should().Contain(item => item.Reason == "ambiguousCeilingUnit" && item.Raw == "2 850");
        projection.Evidence.Should().Contain(item => item.Method == "conflictingValue" && item.Raw == "900 USD");
    }

    [Fact]
    public void MapsJoymeePricesAndMapPointWhileKeepingUnverifiedCodes()
    {
        var projection = Map("joymee.ad.v1", new
        {
            propertyType = 1, dealType = 2, advertiserType = 2,
            detail = new { room_quantity = 2, floor_number = 5, floors_count = 9, area_m2 = "73.25", repair = 4, apartment_type = 1 },
            pricing = new { price = "525.50", currency = 2 }, previousPrice = "550.00",
            location = new { latitude = 41.31, longitude = 69.25 }, district = "Yunusobod",
        });
        projection.Unit.TotalAreaM2.Should().Be(73.25m);
        projection.Place!.Latitude.Should().Be(41.31m);
        projection.Place.Longitude.Should().Be(69.25m);
        projection.Offers[0].Price!.Amount.Should().Be(525.50m);
        projection.Offers[0].Price!.Currency.Should().Be("USD");
        projection.Offers[0].PreviousPrice!.Amount.Should().Be(550m);
        projection.Offers[0].PreviousPrice!.Currency.Should().BeNull();
        projection.Property.Renovation.Should().BeNull();
        projection.UnknownValues.Select(item => item.SourcePath).Should().BeEquivalentTo(
            ["/fields/detail/repair", "/fields/detail/apartment_type", "/fields/advertiserType"]);
    }

    [Fact]
    public void LeavesOtherOlxListingsUnchangedAndExposesVersionedSchema()
    {
        ListingSchemaMapper.Map(Envelope("olx.ad.v1", new { title = "Tablet", isJob = false })).Should().BeNull();
        ListingProjection.JsonSchema.GetProperty("properties").GetProperty("schema")
            .GetProperty("const").GetString().Should().Be(PostEnvelopeConstants.ListingSchema);
        ListingProjection.JsonSchema.GetProperty("required").EnumerateArray().Select(item => item.GetString())
            .Should().Contain(["schema", "property", "unit", "mapping"]);
    }

    private static ListingProjection Map(string schema, object fields) =>
        ListingProjection.Read(ListingSchemaMapper.Map(Envelope(schema, fields))!.Value)!;

    private static PostEnvelope Envelope(string schema, object fields) => new()
    {
        Kind = PostKind.Listing,
        Extras = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(new { schema, parserVersion = "test@1", fields }),
        },
        Extraction = new() { NormVersion = "norm@1", RulesVersion = "rules@4" },
    };
}
