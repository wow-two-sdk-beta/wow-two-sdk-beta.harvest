using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace HarvestAdapters.Tests.Unit.Sources.Olx;

/// <summary>Pins which sections declare the tags their posts already carry: OLX's three rental catalogs, and no other reader's.</summary>
public sealed class OlxSectionImpliedTagsTests
{
    [Fact]
    public void Sections_ShouldImplyAListingAndTheMostSpecificRealEstateNodeThatExists()
    {
        var sections = new OlxSourceAdapter().Sections(Source("www.olx.uz")).ToDictionary(section => section.Key!);

        sections.Keys.Should().Equal("apartments-rent", "houses-rent", "daily-rent");
        sections["apartments-rent"].ImpliedTags.Should().Be(new SectionImpliedTags("listing", "real-estate/apartments"));
        sections["houses-rent"].ImpliedTags.Should().Be(new SectionImpliedTags("listing", "real-estate/houses"));
        sections["daily-rent"].ImpliedTags.Should().Be(new SectionImpliedTags("listing", "real-estate/real-estate-renting-and-leasing"),
            "the daily rentals cover flats, houses and rooms alike, so the node is the rent one");
    }

    [Fact]
    public void Sections_OfOtherReaders_ShouldImplyNothing_SoTheirPostsKeepTheModelPath()
    {
        new AfishaSourceAdapter().Sections(Source("afisha.uz")).Should().OnlyContain(section => section.ImpliedTags == null);
        new JoymeeSourceAdapter().Sections(Source("api.joymee.uz")).Should().OnlyContain(section => section.ImpliedTags == null);
    }

    private static SourceModel Source(string handle) => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Site, Handle = handle,
        Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
        Permission = SourcePermission.Open, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
        ConsentBasis = default, Authority = 50, RetentionClass = "standard", CapabilityPolicy = new(),
        State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };
}
