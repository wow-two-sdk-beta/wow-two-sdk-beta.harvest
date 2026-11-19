using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;

namespace Harvest.Tests.Unit.Entities;

/// <summary>The request rules every form of the entity reads applies.</summary>
public sealed class EntityQueryRulesTests
{
    [Fact]
    public void Normalize_ShouldTrimAndLowercase_AndDropBlanks()
    {
        EntityQueryRules.Normalize("  Style-Fashion ").Should().Be("style-fashion");
        EntityQueryRules.Normalize("   ").Should().BeNull();
        EntityQueryRules.Text("  Visa Fashion Week ").Should().Be("Visa Fashion Week");
        EntityQueryRules.Text(null).Should().BeNull();
    }

    [Theory]
    [InlineData("style-fashion/womens-fashion", "style-fashion", true)]
    [InlineData("style-fashion", "style-fashion", true)]
    [InlineData("style-fashion-x", "style-fashion", false)]
    [InlineData(null, "style-fashion", false)]
    [InlineData(null, null, true)]
    public void AtOrUnder_ShouldKeepTheNodeAndItsChildren(string? node, string? key, bool expected) =>
        EntityQueryRules.AtOrUnder(node, key).Should().Be(expected);

    [Fact]
    public void Rules_ShouldPassAWellFormedRequest()
    {
        EntityQueryRules.Search("Visa", "event", "style-fashion", 20).Should().BeNull();
        EntityQueryRules.Search(null, null, null, 1).Should().BeNull();
        EntityQueryRules.Posts(2024, "report", 200).Should().BeNull();
        EntityQueryRules.Calendar(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 2), "event", null).Should().BeNull("366 days after the first day");
        EntityQueryRules.Events(500, "listing", "real-estate").Should().BeNull();
    }

    [Fact]
    public void Rules_ShouldRefuseEachMalformedValue_AsValidation()
    {
        AppError?[] refused =
        [
            EntityQueryRules.Search(new string('q', 301), null, null, 20), EntityQueryRules.Search("q", "news", null, 20),
            EntityQueryRules.Search("q", null, "style fashion", 20), EntityQueryRules.Search("q", null, null, 101),
            EntityQueryRules.Posts(1899, null, 50), EntityQueryRules.Posts(null, "rumour", 50), EntityQueryRules.Posts(null, null, 201),
            EntityQueryRules.Calendar(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 31), null, null),
            EntityQueryRules.Calendar(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 3), null, null),
            EntityQueryRules.Events(0, null, null), EntityQueryRules.Events(501, null, null), EntityQueryRules.Events(10, null, "Style/"),
        ];

        refused.Should().OnlyContain(error => error != null && error.Type == AppErrorType.Validation);
    }
}
