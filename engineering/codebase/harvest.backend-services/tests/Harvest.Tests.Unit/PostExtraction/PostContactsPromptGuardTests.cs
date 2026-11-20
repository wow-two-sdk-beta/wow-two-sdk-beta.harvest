using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>A post's contacts and author name are stored as plain text, but no listing prompt renders them: the seller number a post
/// holds in its author name, its contacts, its source fields, its title or its text never reaches the model. The listing and the
/// numbers are invented.</summary>
public sealed class PostContactsPromptGuardTests
{
    private const string Number = "+998 90 123 45 67";
    private static readonly string[] Forbidden = ["901234567", "90 123 45 67", "998901234567", "+998"];
    private static readonly ExtractionTags Listing = new("listing", [new PostTagsDomain("real-estate", true)], null, "evergreen", null);

    [Fact]
    public void VariablesFor_ShouldRenderNoSellerNumber_ForAListingWhoseAuthorAndContactsHoldIt()
    {
        var variables = PostExtractionContent.VariablesFor("extract-listing", Post(), "Недвижимость › Аренда", Listing, 6000, section: "apartments-rent");

        variables.Keys.Should().BeEquivalentTo([.. PostExtractionDefinitions.VariablesOf("extract-listing")]);
        foreach (var (name, value) in variables)
            value.Should().NotContainAny(Forbidden, $"the {name} variable is part of the prompt");
        variables["seller"].Should().Be("private", "the seller variable names a category, never the person or a number");
    }

    [Fact]
    public void Prompt_ShouldRenderNoSellerNumber_WhenEveryVariableIsFilledIn()
    {
        var variables = PostExtractionContent.VariablesFor("extract-listing", Post(), "Недвижимость › Аренда", Listing, 6000, section: "apartments-rent");
        string template = string.Join('\n', variables.Keys.Select(name => $"{{{{{name}}}}}"));
        string prompt = variables.Aggregate(template, (text, variable) => text.Replace($"{{{{{variable.Key}}}}}", variable.Value ?? ""));

        prompt.Should().NotContainAny(Forbidden);
        prompt.Should().Contain("Сдаю квартиру").And.Contain("[phone]", "the scrub left a marker where the numbers were");
    }

    [Fact]
    public void Corpus_ShouldHoldNoSellerNumber_SoNoQuoteCanCarryOne()
    {
        var corpus = PostExtractionContent.Corpus(Post(), 6000);

        corpus.Text.Should().NotContainAny(Forbidden);
        foreach (string render in corpus.Structured)
            render.Should().NotContainAny(Forbidden);
        corpus.TermSource().Text.Should().NotContainAny(Forbidden);
    }

    [Fact]
    public void Title_AndText_ShouldStayPhoneScrubbed()
    {
        var post = Post();

        PostTagsContent.Title(post).Should().Be("Сдаю квартиру, [phone]");
        PostTagsContent.Text(post, 6000).Should().Be("Цена 500 USD. Звоните [phone]. Депозит 500 USD.");
        PostExtractionContent.Variables(post, null, Listing, 6000)["text"].Should().NotContainAny(Forbidden);
    }

    private static PostEnvelope Post() => new()
    {
        Kind = PostKind.Listing,
        Title = $"Сдаю квартиру, {Number}",
        Text = $"Цена 500 USD. Звоните {Number}. Депозит 500 USD.",
        Author = new() { Name = Number },
        Contacts = [new() { Kind = ContactKind.Phone, Value = "+998901234567", Raw = Number }],
        Extraction = new() { NormVersion = "test@1", RulesVersion = "test@1" },
        Extras = new Dictionary<string, JsonElement>
        {
            ["source"] = JsonSerializer.SerializeToElement(new
            {
                schema = "olx.ad.v1", parserVersion = "olx-html@2",
                fields = new Dictionary<string, object?>
                {
                    ["externalId"] = "ID0", ["title"] = "Сдаю квартиру", ["sellerName"] = Number, ["isBusiness"] = false,
                    ["attributes"] = new Dictionary<string, string> { ["Этаж"] = "3", ["Контакт"] = Number, ["Описание"] = $"Звоните {Number}" },
                },
            }),
        },
    };
}
