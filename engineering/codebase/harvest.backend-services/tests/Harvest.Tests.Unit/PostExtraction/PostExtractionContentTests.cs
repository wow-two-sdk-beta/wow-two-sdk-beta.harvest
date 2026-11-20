using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>What an extraction prompt receives: the post's words phone-scrubbed, the quote corpus built from the same scrubbed text,
/// and for a listing the three variables an OLX ad fills (<c>attributes</c>, <c>section</c>, <c>seller</c>) and an Afisha post
/// leaves empty. The listings and numbers are invented.</summary>
public sealed class PostExtractionContentTests
{
    private static readonly ExtractionTags Listing = new("listing", [new PostTagsDomain("real-estate", true)], null, "evergreen", null);
    private static readonly ExtractionTags Event = new("event", [new PostTagsDomain("style-fashion", true)], null, "upcoming", null);
    private static readonly ExtractionStamp Stamp = new() { NormVersion = "test@1", RulesVersion = "test@1" };

    [Fact]
    public void VariablesFor_ShouldRenderTheThreeListingVariables_ForAnOlxAd()
    {
        var olx = Olx(new Dictionary<string, string>
        {
            ["Количество комнат"] = "2", ["Общая площадь"] = "54 м²", ["Этаж"] = "3", ["Комиссионные"] = "Нет", ["Бизнес"] = "Бизнес",
        }, isBusiness: null);

        var variables = PostExtractionContent.VariablesFor("extract-listing", olx, "Недвижимость › Аренда", Listing, 6000, section: "apartments-rent");

        variables["attributes"].Should().Be("Количество комнат: 2\nОбщая площадь: 54 м²\nЭтаж: 3\nКомиссионные: Нет\nБизнес");
        variables["section"].Should().Be("apartments-rent");
        variables["seller"].Should().Be("business", "the badge among the attributes names the seller when the ad has no flag");
        variables.Keys.Should().BeEquivalentTo([.. PostExtractionDefinitions.VariablesOf("extract-listing")]);
    }

    [Fact]
    public void VariablesFor_ShouldRenderAttributesAndSellerEmpty_ForAnAfishaPost_ButStillNameItsSection()
    {
        var afisha = new PostEnvelope
        {
            Title = "Tashkent Fashion Week", Text = "Moda haftaligi dekabrda.", Extraction = Stamp,
            Extras = new Dictionary<string, JsonElement>
            {
                ["source"] = JsonSerializer.SerializeToElement(new { schema = "afisha.event.v1", fields = new { attributes = new { Hall = "A" } } }),
            },
        };

        var variables = PostExtractionContent.VariablesFor("extract-listing", afisha, null, Listing, 6000, section: "afisha-events");

        variables["attributes"].Should().BeEmpty("only an olx.ad.v1 post carries attributes");
        variables["seller"].Should().BeEmpty("only an olx.ad.v1 post carries a seller");
        variables["section"].Should().Be("afisha-events", "the section names any source's catalog key or pipeline");
        var unknown = PostExtractionContent.VariablesFor("extract-listing", afisha, null, Listing, 6000);
        unknown["section"].Should().BeEmpty("a post whose section is not known renders it empty");
        PostExtractionContent.VariablesFor("extract-listing", new PostEnvelope { Extraction = Stamp }, null, Listing, 6000)["attributes"].Should().BeEmpty();
    }

    [Fact]
    public void VariablesFor_ShouldAddNothingForAnEventPass()
    {
        var variables = PostExtractionContent.VariablesFor("extract-event", Olx(new Dictionary<string, string> { ["Этаж"] = "3" }), null, Event, 6000,
            section: "apartments-rent");

        variables.Keys.Should().BeEquivalentTo([.. PostExtractionDefinitions.Variables]);
        variables.Should().NotContainKeys("attributes", "section", "seller");
    }

    [Theory]
    [InlineData(true, "business")]
    [InlineData(false, "private")]
    public void Seller_ShouldReadTheAdsOwnFlagFirst(bool isBusiness, string expected)
    {
        var attributes = new Dictionary<string, string> { [isBusiness ? "Частное лицо" : "Бизнес"] = "x" };

        PostExtractionContent.Seller(Olx(attributes, isBusiness)).Should().Be(expected, "the flag wins over a badge that disagrees");
    }

    [Theory]
    [InlineData("Бизнес", "business")]
    [InlineData("Частное лицо", "private")]
    [InlineData("Biznes", "business")]
    [InlineData("Jismoniy shaxs", "private")]
    [InlineData("Комиссионные", "unknown")]
    public void Seller_ShouldReadTheBadgeAmongTheAttributes_ElseUnknown(string label, string expected)
    {
        PostExtractionContent.Seller(Olx(new Dictionary<string, string> { [label] = label })).Should().Be(expected);
        PostExtractionContent.Seller(Olx(new Dictionary<string, string> { ["Объявление от"] = label })).Should().Be(expected);
    }

    [Fact]
    public void Attributes_ShouldKeepTheSourceLabelsAndOrder_ScrubPhones_AndSkipBlankLines()
    {
        var olx = Olx(new Dictionary<string, string>
        {
            ["Этаж"] = "3", ["Телефон"] = "+998 90 123 45 67", ["  "] = "", ["Описание"] = "Звоните\n 93 765 43 21", ["Год постройки"] = "2019",
        });

        PostExtractionContent.Attributes(olx).Should().Be("Этаж: 3\nТелефон: [phone]\nОписание: Звоните [phone]\nГод постройки: 2019");
    }

    [Fact]
    public void Attributes_ShouldStopAtFortyLines_AndClipLongOnes()
    {
        var many = Enumerable.Range(1, 60).ToDictionary(index => $"Поле {index}", _ => new string('я', 300));

        var lines = PostExtractionContent.Attributes(Olx(many)).Split('\n');

        lines.Should().HaveCount(40);
        lines.Should().OnlyContain(line => line.Length <= 210);
    }

    [Fact]
    public void Variables_ShouldScrubTheTitleAndText_BeforeTheyAreCut()
    {
        var post = new PostEnvelope
        {
            Title = "Сдаю квартиру, +998 90 123 45 67", Text = "Цена 500 USD. Звоните 93 765 43 21." + new string('а', 40), Extraction = Stamp,
        };

        var variables = PostExtractionContent.Variables(post, null, Listing, 200);

        variables["title"].Should().Be("Сдаю квартиру, [phone]");
        variables["text"].Should().StartWith("Цена 500 USD. Звоните [phone].").And.NotContain("765 43 21");
        PostExtractionContent.Variables(post, null, Listing, 33)["text"].Should().Be("Цена 500 USD. Звоните [phone].".PadRight(33, 'а')[..33]);
    }

    [Fact]
    public void Corpus_ShouldCheckQuotesAgainstTheSameScrubbedText_ThePromptReceives()
    {
        var post = new PostEnvelope
        {
            Title = "Сдаю квартиру", Text = "Цена 500 USD. Звоните +998 90 123 45 67. Депозит 500 USD.", Extraction = Stamp,
        };

        var corpus = PostExtractionContent.Corpus(post, 6000);
        var sent = PostExtractionContent.Variables(post, null, Listing, 6000);

        corpus.Text.Should().Be($"{sent["title"]}\n{sent["text"]}");
        corpus.Text.Should().Contain("Звоните [phone].").And.NotContain("+998");
        corpus.Visible.Should().Be(corpus.Text.Length);
        RentalTermEvidence.Quoted("Депозит 500 USD", corpus.Text, corpus.Visible).Should().BeTrue();
        RentalTermEvidence.Quoted("+998 90 123 45 67", corpus.Text, corpus.Visible).Should().BeFalse("a number the prompt never held is no quote");
    }

    [Fact]
    public void Corpus_ShouldHoldTheAttributesRender_AsASourceOfItsOwn()
    {
        var corpus = PostExtractionContent.Corpus(Olx(new Dictionary<string, string> { ["Комиссионные"] = "Нет" }), 6000);

        corpus.Attributes.Should().Be("Комиссионные: Нет");
        corpus.Structured.Should().Contain("Комиссионные: Нет");
        corpus.TermSource().Text.Should().EndWith("Комиссионные: Нет").And.Contain(corpus.Text[..corpus.Visible]);
        PostExtractionContent.Corpus(new PostEnvelope { Title = "x", Extraction = Stamp }, 6000).Attributes.Should().BeNull();
    }

    [Fact]
    public void Section_ShouldReadEmptyWhenNoneIsKnown()
    {
        PostExtractionContent.Section(null).Should().BeEmpty();
        PostExtractionContent.Section("  ").Should().BeEmpty();
        PostExtractionContent.Section(" daily-rent ").Should().Be("daily-rent");
    }

    private static PostEnvelope Olx(Dictionary<string, string> attributes, bool? isBusiness = null) => new()
    {
        Title = "Сдается 2-комнатная квартира", Text = "Цена 500 USD в месяц.", Extraction = Stamp,
        Extras = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(new
            {
                schema = "olx.ad.v1", parserVersion = "olx-html@2",
                fields = new Dictionary<string, object?>
                {
                    ["externalId"] = "ID0", ["title"] = "Сдается 2-комнатная квартира", ["sellerName"] = "Seller Name", ["attributes"] = attributes,
                    ["isBusiness"] = isBusiness,
                },
            }),
        },
    };
}
