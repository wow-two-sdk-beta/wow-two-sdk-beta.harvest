using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace Harvest.Tests.Unit.Engine;

public sealed class PipelineScopeTests
{
    [Theory]
    [InlineData("https://www.afisha.uz/ru/concerts?date=2026-11-10", true)]
    [InlineData("https://www.afisha.uz/ru/concerts/2026/11/10/live", true)]
    [InlineData("https://www.afisha.uz/ru/concerts-archive/live", false)]
    [InlineData("https://www.afisha.uz/ru/exhibitions/2026/11/10/show", false)]
    [InlineData("https://other.example/ru/concerts", false)]
    public void IncludesUrl_ShouldBoundAnExplicitSection(string url, bool expected)
    {
        var scope = new PipelineScope { Url = "https://www.afisha.uz/ru/concerts/", RestrictToPath = true };

        scope.IncludesUrl(url).Should().Be(expected);
        (scope with { RestrictToPath = false }).IncludesUrl(url).Should().BeTrue();
    }
}
