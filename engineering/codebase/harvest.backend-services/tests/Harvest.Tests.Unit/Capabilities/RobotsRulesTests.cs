using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

namespace Harvest.Tests.Unit.Capabilities;

public sealed class RobotsRulesTests
{
    [Fact]
    public void Allows_ShouldUseSpecificGroupsAndLongestPaths_WhenRulesOverlap()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /
            User-agent: Harvest
            Disallow: /private
            Allow: /private/open
            Disallow: /*?draft=1$
            Crawl-delay: 7.5
            User-agent: Harvest
            Allow: /private/open
            """, "Harvest/0.1 (+https://example.com)");

        rules.Allows(new Uri("https://example.com/public")).Should().BeTrue();
        rules.Allows(new Uri("https://example.com/private/closed")).Should().BeFalse();
        rules.Allows(new Uri("https://example.com/private/open/a")).Should().BeTrue();
        rules.Allows(new Uri("https://example.com/public?draft=1")).Should().BeFalse();
        rules.Allows(new Uri("https://example.com/public?draft=10")).Should().BeTrue();
        rules.CrawlDelay.Should().Be(TimeSpan.FromSeconds(7.5));
    }

    [Fact]
    public void Allows_ShouldPreferAllowAtEqualSpecificity_AndIgnoreEmptyDisallow()
    {
        var rules = RobotsRules.Parse("User-agent: *\nDisallow:\nDisallow: /a\nAllow: /a", "Harvest/0.1");
        rules.Allows(new Uri("https://example.com/a")).Should().BeTrue();
        rules.Allows(new Uri("https://example.com/b")).Should().BeTrue();
    }

    [Fact]
    public void Allows_ShouldNormalizeUtf8AndUnreservedPercentEncoding()
    {
        var rules = RobotsRules.Parse("\uFEFFUser-agent: *\nDisallow: /~private\nDisallow: /каталог", "Harvest/0.1");
        rules.Allows(new Uri("https://example.com/%7eprivate/a")).Should().BeFalse();
        rules.Allows(new Uri("https://example.com/%D0%BA%D0%B0%D1%82%D0%B0%D0%BB%D0%BE%D0%B3/a")).Should().BeFalse();
    }
}
