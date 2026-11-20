using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace HarvestAdapters.Tests.Unit.Sources.Olx;

/// <summary>Pins the image hosts a source declares: OLX serves its photos from olxcdn.com, and no other reader widens its own host.</summary>
public sealed class OlxMediaHostsTests
{
    [Fact]
    public void Describe_ShouldDeclareTheOlxCdnWithTheFirstSixImagesAndATwoSecondGap()
    {
        var media = new OlxSourceAdapter().Describe(Source("www.olx.uz")).Media;

        media.Should().NotBeNull();
        media!.Domains.Should().Equal("olxcdn.com");
        media.Limit.Should().Be(6);
        media.MinDelay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Describe_OfOtherReaders_ShouldDeclareNoMedia_SoTheyCopyOnlyTheirOwnHost()
    {
        new AfishaSourceAdapter().Describe(Source("afisha.uz")).Media.Should().BeNull();
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
