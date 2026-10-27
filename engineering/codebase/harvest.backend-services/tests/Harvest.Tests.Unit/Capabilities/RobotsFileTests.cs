using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Robots;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>Reading a host's <c>robots.txt</c>: which paths our agent may fetch, and the crawl delay it asks for. A
/// disallowed path is an outcome, never worked around.</summary>
public sealed class RobotsFileTests
{
    private const string Agent = "harvestbot";

    [Fact]
    public void IsAllowed_ShouldObeyADisallow_WhenTheStarGroupForbidsThePath()
    {
        var robots = RobotsFile.Parse("User-agent: *\nDisallow: /private\n");

        robots.IsAllowed("/private/report", Agent).Should().BeFalse();
        robots.IsAllowed("/public/report", Agent).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_ShouldLetTheMostSpecificRuleWin_WhenAllowReopensASubtree()
    {
        var robots = RobotsFile.Parse("User-agent: *\nDisallow: /events\nAllow: /events/public\n");

        robots.IsAllowed("/events/secret", Agent).Should().BeFalse();
        robots.IsAllowed("/events/public/today", Agent).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_ShouldAnchorTheEnd_WhenThePatternEndsWithDollar()
    {
        var robots = RobotsFile.Parse("User-agent: *\nDisallow: /*.json$\n");

        robots.IsAllowed("/data/feed.json", Agent).Should().BeFalse();
        robots.IsAllowed("/data/feed.jsonp", Agent).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_ShouldPreferTheNamedGroup_WhenOneMatchesOurAgent()
    {
        var robots = RobotsFile.Parse("User-agent: *\nDisallow: /\n\nUser-agent: HarvestBot\nDisallow: /admin\n");

        robots.IsAllowed("/ru/events", Agent).Should().BeTrue();
        robots.IsAllowed("/admin/users", Agent).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_ShouldAllowEverything_WhenDisallowIsEmpty()
    {
        var robots = RobotsFile.Parse("User-agent: *\nDisallow:\n");

        robots.IsAllowed("/anything", Agent).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_ShouldAllowEverything_WhenTheFileIsMissing()
    {
        RobotsFile.AllowAll.IsAllowed("/anything", Agent).Should().BeTrue();
    }

    [Fact]
    public void CrawlDelay_ShouldReadTheGroupsValue_WhenItIsStated()
    {
        var robots = RobotsFile.Parse("User-agent: *\nCrawl-delay: 10\nDisallow: /x\n");

        robots.CrawlDelay(Agent).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CrawlDelay_ShouldBeNull_WhenNoGroupStatesOne()
    {
        RobotsFile.Parse("User-agent: *\nDisallow: /x\n").CrawlDelay(Agent).Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldCollectSitemaps_WhenTheyAreListed()
    {
        var robots = RobotsFile.Parse("Sitemap: https://afisha.uz/sitemap.xml\nUser-agent: *\nDisallow:\n");

        robots.Sitemaps.Should().ContainSingle().Which.Should().Be("https://afisha.uz/sitemap.xml");
    }

    [Fact]
    public void Parse_ShouldIgnoreCommentsAndBlankLines_WhenTheyAppear()
    {
        var robots = RobotsFile.Parse("# a comment\n\nUser-agent: *   # inline\nDisallow: /private  # keep out\n");

        robots.IsAllowed("/private/x", Agent).Should().BeFalse();
        robots.IsAllowed("/open", Agent).Should().BeTrue();
    }
}
