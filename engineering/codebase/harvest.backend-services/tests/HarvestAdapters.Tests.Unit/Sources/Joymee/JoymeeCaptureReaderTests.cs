using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;

namespace HarvestAdapters.Tests.Unit.Sources.Joymee;

public sealed class JoymeeCaptureReaderTests
{
    private const string Detail = "https://api.joymee.uz/api/v1/announcement/1842/";
    private const string Fixture = """
        {
          "id": 1842,
          "title": "Apartment for rent",
          "description": "Synthetic apartment listing",
          "status": 1,
          "category": 4,
          "deal_type": 2,
          "property_type": 1,
          "address_line": "Synthetic street, Tashkent",
          "ads_at": "2026-11-14T08:10:00+05:00",
          "advertiser_type": 2,
          "previous_price": "500.20",
          "pricing": { "price": "450.25", "currency": 2, "currency_display": "USD", "price_from": null, "price_to": null, "secret": "excluded-joymee-field" },
          "detail": { "room_quantity": 2, "area_m2": "54.25", "floor_number": 3, "floors_count": 9, "repair": 4, "apartment_type": 99, "secret": "excluded-joymee-field" },
          "location": { "latitude": 41.115, "longitude": null, "secret": "excluded-joymee-field" },
          "region": { "id": 59, "name": "Toshkent" },
          "district": { "id": 10, "name": "District" },
          "seller": { "first_name": "Sample", "last_name": "Publisher", "is_verified": false, "username": "excluded-joymee-field" },
          "media": [
            { "file": { "id": 45, "url": "https://cdn.joymeemedia.uz/listing.jpg", "private": "excluded-joymee-field" }, "order": 5, "is_cover": true },
            { "file": { "id": 46, "url": "https://other.example/excluded-joymee-field.jpg" }, "order": 1, "is_cover": false }
          ],
          "stats": { "views": 10 },
          "phone_number": "excluded-joymee-field",
          "is_liked": true
        }
        """;

    [Fact]
    public async Task Read_ShouldKeepNativeListingFactsWithoutViewerAndPrivateFields()
    {
        var capture = await Read(Fixture);
        JsonElement source = capture.Structured[1].GetProperty("sourceFields");
        JsonElement fields = source.GetProperty("fields");
        JsonElement schema = new JoymeeSourceAdapter().DescribeConfiguration(null!).SourceFieldsSchema!.Value.GetProperty("properties");
        JsonElement declaredFields = schema.GetProperty("fields").GetProperty("properties");

        source.GetProperty("schema").GetString().Should().Be(schema.GetProperty("schema").GetProperty("const").GetString());
        source.GetProperty("parserVersion").GetString().Should().Be("joymee-json@2");
        fields.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(declaredFields.EnumerateObject().Select(property => property.Name));
        fields.GetProperty("externalId").GetString().Should().Be(capture.ExternalKey);
        fields.GetProperty("addressLine").GetString().Should().Be("Synthetic street, Tashkent");
        fields.GetProperty("advertiserType").GetInt32().Should().Be(2);
        fields.GetProperty("publisherVerified").GetBoolean().Should().BeFalse();
        fields.GetProperty("previousPrice").GetString().Should().Be("500.20");
        fields.GetProperty("detail").GetProperty("apartment_type").GetInt32().Should().Be(99);
        fields.GetProperty("detail").GetProperty("area_m2").GetString().Should().Be("54.25");
        fields.GetProperty("pricing").GetProperty("currency").GetInt32().Should().Be(2);
        fields.GetProperty("location").GetProperty("longitude").ValueKind.Should().Be(JsonValueKind.Null);
        JsonElement media = fields.GetProperty("media").EnumerateArray().Single();
        media.GetProperty("file").GetProperty("id").GetInt32().Should().Be(45);
        media.GetProperty("order").GetInt32().Should().Be(5);
        media.GetProperty("is_cover").GetBoolean().Should().BeTrue();
        capture.Media.Should().ContainSingle().Which.Url.Should().Be("https://cdn.joymeemedia.uz/listing.jpg");
        capture.Structured[0].GetProperty("offers").GetProperty("price").GetString().Should().Be("450.25");
        fields.GetProperty("attributes").GetProperty("Этаж").GetString().Should().Be("3");
        source.GetRawText().Should().NotContain("excluded-joymee-field");
        capture.ContentProjection.Should().NotContain("excluded-joymee-field");
    }

    [Fact]
    public async Task Read_ShouldVersionChangesToListingFactsWhileIgnoringCounters()
    {
        var original = await Read(Fixture);
        var newView = await Read(Fixture.Replace("\"views\": 10", "\"views\": 11", StringComparison.Ordinal));
        var newRepair = await Read(Fixture.Replace("\"repair\": 4", "\"repair\": 5", StringComparison.Ordinal));

        newView.ContentProjection.Should().Be(original.ContentProjection);
        newRepair.ContentProjection.Should().NotBe(original.ContentProjection);
    }

    private static async Task<Capture> Read(string content)
    {
        Result<CaptureReadResult> result = await new JoymeeCaptureReader().ReadAsync(new()
        {
            RequestUrl = new Uri(Detail),
            FinalUrl = new Uri(Detail),
            Body = Encoding.UTF8.GetBytes(content),
            ContentType = "application/json",
            StatusCode = 200,
            FetchedAtUtc = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
        });
        return result.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value.Captures.Single();
    }
}
