using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Unit.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Unit.Extraction;

/// <summary>Checks that a phone a seller published in the display name or in the listing attributes reaches the post's contacts as
/// plain E.164 text, with nothing removed from the author, the title or the text. The captures mirror what the OLX reader writes;
/// every number is invented.</summary>
public sealed class SellerPhonesExtractionTests
{
    private const string OlxAd = "https://www.olx.uz/d/obyavlenie/desk-IDAb12.html";
    private const string OtherAd = "https://listings.example/d/desk-IDAb12.html";

    [Fact]
    public async Task Extract_ShouldReadAPhoneFromTheSellerName_AndKeepTheAuthorTitleAndTextAsTheyAre()
    {
        var post = await Extract(Olx("+998 90 123 45 67"));

        var contact = post.Contacts.Should().ContainSingle().Which;
        contact.Kind.Should().Be(ContactKind.Phone);
        contact.Value.Should().Be("+998901234567");
        contact.Raw.Should().Be("+998 90 123 45 67");
        post.FieldSources["contacts"].Tier.Should().Be(ExtractionTier.Rules);
        post.FieldSources["contacts"].Rule.Should().Be("e164-and-email@3");
        post.Author!.Name.Should().Be("+998 90 123 45 67");
        post.Title.Should().Be("Oak desk");
        post.Text.Should().Be("Full description.");
        post.Extraction.RulesVersion.Should().Be("rules@5", "the golden event pages carry rules@5 and Events replays them");
    }

    [Theory]
    [InlineData("90-123-45-67", "+998901234567")]
    [InlineData("(90) 123 45 67", "+998901234567")]
    [InlineData("93 765 43 21", "+998937654321")]
    public async Task Extract_ShouldReadALocalNumberInTheSellerName_ForAnUzbekSource(string seller, string expected)
    {
        var post = await Extract(Olx(seller));

        post.Contacts.Should().ContainSingle().Which.Value.Should().Be(expected);
        post.Author!.Name.Should().Be(seller);
    }

    [Theory]
    [InlineData("Rustam")]
    [InlineData("Rustam 2")]
    [InlineData("Мебель 24")]
    public async Task Extract_ShouldFindNoContact_WhenTheSellerNameHoldsNoNumber(string seller)
    {
        var post = await Extract(Olx(seller));

        post.Contacts.Should().BeEmpty();
        post.FieldSources.Should().NotContainKey("contacts");
        post.Author!.Name.Should().Be(seller);
    }

    [Fact]
    public async Task Extract_ShouldReadAPhoneFromAnAttributeValue()
    {
        var post = await Extract(Olx("Rustam", new Dictionary<string, string> { ["Этаж"] = "3", ["Контакт"] = "+998 93 765 43 21" }));

        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998937654321");
        post.FieldSources["contacts"].Rule.Should().Be("e164-and-email@3");
    }

    [Theory]
    [InlineData("Общая площадь", "65")]
    [InlineData("Общая площадь", "65 м²")]
    [InlineData("Цена", "450 000 000")]
    [InlineData("Депозит", "450 000 000 сум")]
    [InlineData("Год постройки", "2019")]
    public async Task Extract_ShouldFindNoContact_InAnAttributeThatIsNoPhone(string label, string value)
    {
        var post = await Extract(Olx("Rustam", new Dictionary<string, string> { [label] = value, ["Этаж"] = "3" }));

        post.Contacts.Should().BeEmpty();
        post.FieldSources.Should().NotContainKey("contacts");
    }

    [Fact]
    public async Task Extract_ShouldReadEachAttributeOnItsOwn_NeverJoiningNeighbouringValuesIntoANumber()
    {
        var post = await Extract(Olx("Rustam", new Dictionary<string, string> { ["Комнат"] = "90", ["Номер"] = "123 45 67" }));

        post.Contacts.Should().BeEmpty("a number is read inside one value, not across the end of one and the start of the next");
    }

    [Fact]
    public async Task Extract_ShouldReadAttributeValuesDefensively_WhateverShapeTheyHave()
    {
        var attributes = new Dictionary<string, object?>
        {
            ["Контакты"] = new object?[] { 7, null, true, "+998 93 765 43 21", new { Note = new[] { "Звоните +998 90 123 45 67" } } },
            ["Этаж"] = 3, ["Пусто"] = null,
        };

        var post = await Extract(Olx("Rustam", attributes));

        post.Contacts.Select(contact => contact.Value).Should().BeEquivalentTo("+998937654321", "+998901234567");
    }

    [Theory]
    [InlineData("""null""")]
    [InlineData("""[]""")]
    [InlineData("\"no attributes\"")]
    [InlineData("""12""")]
    [InlineData("""{}""")]
    public async Task Extract_ShouldNotFail_WhenTheAttributesHaveNoStrings(string json)
    {
        var post = await Extract(Olx("Rustam", JsonDocument.Parse(json).RootElement.Clone()));

        post.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_ShouldNotFail_WhenTheSourceFieldsAreShapedOtherwise()
    {
        var metadata = JsonSerializer.SerializeToElement(new { reader = "page", title = "Oak desk", text = "Full description.", sourceFields = "none" });

        var post = await Extract(CaptureOf(OlxAd, [Product("Rustam", "Full description."), metadata]));

        post.Contacts.Should().BeEmpty();
    }

    [Fact]
    public async Task Extract_ShouldKeepOneContact_WhenATelLinkAndTheSellerNameGiveTheSameNumber()
    {
        var post = await Extract(Olx("+998 90 123 45 67", contacts: ["+998901234567"]));

        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998901234567");
    }

    [Fact]
    public async Task Extract_ShouldKeepOneContact_WhenTheSellerNameTheAttributeAndTheTextGiveTheSameNumber()
    {
        var post = await Extract(Olx("90-123-45-67", new Dictionary<string, string> { ["Телефон"] = "+998 90 123 45 67" },
            text: "Звоните 90 123 45 67."));

        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998901234567");
    }

    [Fact]
    public async Task Extract_ShouldNotAddTheUzbekLocalRule_ToAnAuthorNameOnAnotherSource()
    {
        var post = await Extract(Olx("90 123 45 67", url: OtherAd));

        post.Contacts.Should().BeEmpty("a 9-digit local number gains +998 only for an Uzbek source");
        post.Author!.Name.Should().Be("90 123 45 67");
    }

    [Fact]
    public async Task Extract_ShouldReadTheNameThatTheSchemaOrgSellerGives_WithoutReaderMetadata()
    {
        var post = await Extract(CaptureOf(OlxAd, [Product("+998 90 123 45 67", "Full description.")]));

        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998901234567");
        post.Author!.Name.Should().Be("+998 90 123 45 67");
    }

    [Fact]
    public async Task Extract_ShouldReadTheNameThatTheMetadataAuthorGives_WhenTheStructuredNodeNamesNoSeller()
    {
        var post = await Extract(CaptureOf(OlxAd, [Product(null, "Full description."), Metadata("+998 90 123 45 67", null, null, null, "Full description.")]));

        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998901234567");
        post.Author!.Name.Should().Be("+998 90 123 45 67");
    }

    [Fact]
    public async Task Extract_ShouldReadTheNameThatThePageMetadataGives_WithoutAStructuredNode()
    {
        var post = await Extract(CaptureOf(OlxAd, [Metadata("+998 90 123 45 67", null, null, null, "Full description.")]));

        post.Extras.Should().ContainKey("openGraph");
        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998901234567");
        post.Author!.Name.Should().Be("+998 90 123 45 67");
    }

    private static async Task<PostEnvelope> Extract(Capture capture) =>
        (await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site })).Data();

    /// <summary>Builds the capture an OLX ad reads into: the normalized product with the seller as its offer's seller, and the
    /// metadata node with the listing attributes under <c>sourceFields.fields.attributes</c>.</summary>
    private static Capture Olx(string seller, object? attributes = null, string[]? contacts = null, string url = OlxAd, string text = "Full description.") =>
        CaptureOf(url, [Product(seller, text), Metadata(seller, attributes ?? new Dictionary<string, string> { ["Состояние"] = "Б/у" }, contacts ?? [], "olx.ad.v1", text)]);

    private static JsonElement Product(string? seller, string description) => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
    {
        ["@type"] = "Product", ["name"] = "Oak desk", ["description"] = description, ["url"] = OlxAd,
        ["offers"] = seller is null ? new { price = "700", priceCurrency = "USD" }
            : (object)new { price = "700", priceCurrency = "USD", seller = new { name = seller } },
    });

    private static JsonElement Metadata(string? author, object? attributes, string[]? contacts, string? schema, string text) => JsonSerializer.SerializeToElement(new
    {
        reader = "page", source = "olx-uz", title = "Oak desk", text, author, contacts = contacts ?? [],
        sourceFields = new { schema = schema ?? "olx.ad.v1", parserVersion = "olx-html@2", fields = new { attributes } },
    });

    private static Capture CaptureOf(string url, JsonElement[] structured) => new()
    {
        ExternalKey = "IDAb12", Url = url, RequestUrl = url, FinalUrl = url, Body = "fixture"u8.ToArray(), ContentProjection = "olx",
        CapturedAtUtc = DateTimeOffset.UtcNow, Structured = structured,
    };
}
