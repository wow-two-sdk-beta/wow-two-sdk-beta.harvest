using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>The section a listing prompt names: the adapter catalog's key when the pipeline's address is one of its sections.</summary>
public sealed class PostSectionsTests
{
    private const string Apartments = "https://www.olx.uz/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/?search%5Border%5D=created_at%3Adesc";
    private const string Houses = "https://www.olx.uz/nedvizhimost/doma/arenda-dolgosrochnaya/tashkent/?search%5Border%5D=created_at%3Adesc";
    private const string Daily = "https://www.olx.uz/nedvizhimost/posutochno_pochasovo/tashkent/?search%5Border%5D=created_at%3Adesc";

    private static readonly SourceModel Source = new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Site, Handle = "olx.uz",
        Coverage = default, Completeness = default, Posture = default, Permission = default,
        AiAllowed = true, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, ConsentBasis = default,
        Authority = 50, RetentionClass = "default", CapabilityPolicy = new(), State = default,
        StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    [Theory]
    [InlineData(Apartments, "apartments-rent")]
    [InlineData(Houses, "houses-rent")]
    [InlineData(Daily, "daily-rent")]
    [InlineData("https://olx.uz/nedvizhimost/posutochno_pochasovo/tashkent/?search%5Border%5D=created_at%3Adesc", "daily-rent")]
    [InlineData("https://www.olx.uz/nedvizhimost/posutochno_pochasovo/tashkent/?search%5Border%5D=created_at%3Adesc&page=2", null)]
    [InlineData("https://www.olx.uz/nedvizhimost/kvartiry/prodazha/tashkent/", null)]
    [InlineData("https://other.example/nedvizhimost/posutochno_pochasovo/tashkent/?search%5Border%5D=created_at%3Adesc", null)]
    [InlineData("not an address", null)]
    public void Key_ShouldNameTheCatalogSectionWhoseAddressThePipelineHas(string address, string? expected)
    {
        PostSections.Key(new Catalog(), Source, address).Should().Be(expected);
    }

    [Fact]
    public void Key_ShouldBeNone_WithoutACatalog()
    {
        PostSections.Key(null, Source, Apartments).Should().BeNull();
    }

    [Theory]
    [InlineData("https://www.olx.uz/a/b/", "https://olx.uz/a/b", true)]
    [InlineData("https://www.olx.uz/a/b?x=1", "https://www.olx.uz/a/b?x=2", false)]
    [InlineData("https://www.olx.uz/a/b", null, false)]
    public void SameSection_ShouldIgnoreWwwAndATrailingSlash_ButNotThePathOrQuery(string expected, string? actual, bool same)
    {
        PostSections.SameSection(expected, actual).Should().Be(same);
    }

    private sealed class Catalog : ISourceSectionCatalog
    {
        public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) =>
        [
            new("Tashkent long-term apartments", Apartments, "und") { Key = "apartments-rent", RestrictToPath = false, RegisterPassivePipeline = false },
            new("Tashkent long-term houses", Houses, "und") { Key = "houses-rent", RestrictToPath = false, RegisterPassivePipeline = false },
            new("Tashkent daily rentals", Daily, "und") { Key = "daily-rent", RestrictToPath = false, RegisterPassivePipeline = false },
        ];
    }
}
