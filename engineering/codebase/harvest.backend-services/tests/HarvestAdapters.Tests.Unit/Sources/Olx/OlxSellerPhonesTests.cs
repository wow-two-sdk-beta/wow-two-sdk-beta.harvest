using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace HarvestAdapters.Tests.Unit.Sources.Olx;

/// <summary>Reads an OLX ad through the real reader and extractor, to show that the phones a seller published in the display name,
/// the listing attributes and a <c>tel:</c> link reach the post's contacts as plain E.164 text. The numbers are invented.</summary>
public sealed class OlxSellerPhonesTests
{
    private const string Ad = "https://www.olx.uz/d/oz/obyavlenie/desk-IDAb12.html";

    [Fact]
    public async Task Read_ShouldLetTheSellerNameAndTheAttributesGiveContacts_WithoutRevealingAnything()
    {
        string html = Page("+998 90 123 45 67", "<p>Этаж: 3</p><p>Общая площадь: 65</p><p>Контакт: 93 765 43 21</p><p>Цена: 450 000 000</p>",
            "<p>Full description. <a href='tel:+998901234567'>Call</a></p>");
        Capture capture = Value(await new OlxCaptureReader().ReadAsync(Document(html))).Captures.Single();
        PostEnvelope post = Value(await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site }));

        post.Contacts.Select(contact => contact.Value).Should().BeEquivalentTo(["+998901234567", "+998937654321"],
            "the tel: link and the seller name give one number, and the attribute gives the other");
        post.Contacts.Should().OnlyContain(contact => contact.Kind == ContactKind.Phone);
        post.FieldSources["contacts"].Rule.Should().Be("e164-and-email@3");
        post.Author!.Name.Should().Be("+998 90 123 45 67");
        post.Extras["source"].GetProperty("fields").GetProperty("sellerName").GetString().Should().Be("+998 90 123 45 67");
        post.Extraction.RulesVersion.Should().Be("rules@5");
    }

    [Fact]
    public async Task Read_ShouldFindNoContact_WhenTheSellerNameAndTheAttributesHoldNoPhone()
    {
        string html = Page("Rustam", "<p>Этаж: 3</p><p>Общая площадь: 65</p><p>Цена: 450 000 000</p>", "<p>Full description.</p>");
        Capture capture = Value(await new OlxCaptureReader().ReadAsync(Document(html))).Captures.Single();
        PostEnvelope post = Value(await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site }));

        post.Contacts.Should().BeEmpty();
        post.FieldSources.Should().NotContainKey("contacts");
    }

    private static string Page(string seller, string parameters, string description) =>
        "<html lang='ru'><div data-testid='offer_title'><h4>Oak desk</h4></div>"
        + "<div data-testid='ad_description'><h3>Description</h3><div>" + description + "</div></div>"
        + "<div data-testid='ad-price-container'><h3>700 USD</h3></div><h4 data-testid='user-profile-user-name'>" + seller + "</h4>"
        + "<div data-testid='ad-parameters-container'>" + parameters + "</div></html>";

    private static FetchedDocument Document(string html) => new()
    {
        RequestUrl = new Uri(Ad), FinalUrl = new Uri(Ad), StatusCode = 200, ContentType = "text/html",
        Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = DateTimeOffset.UtcNow,
    };

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
