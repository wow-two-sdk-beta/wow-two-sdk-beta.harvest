using System.Text.RegularExpressions;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace Harvest.Tests.Unit.Tagging;

/// <summary>The committed IAB 3.1 seed in <c>011-taxonomy</c>: 34 domains, their descendants, nothing of the three
/// excluded tier-1 entries, and a tree whose every key is a slug path under an existing parent.</summary>
public sealed partial class TaxonomySeedTests
{
    private static readonly string[] Domains =
    [
        "Attractions", "Automotive", "Books and Literature", "Business and Finance", "Careers", "Communication", "Crime",
        "Disasters", "Education", "Entertainment", "Family and Relationships", "Fine Art", "Food & Drink", "Healthy Living",
        "Hobbies & Interests", "Holidays", "Home & Garden", "Law", "Medical Health", "Personal Celebrations & Life Events",
        "Personal Finance", "Pets", "Politics", "Pop Culture", "Real Estate", "Religion & Spirituality", "Science", "Shopping",
        "Sports", "Style & Fashion", "Technology & Computing", "Travel", "Video Gaming", "War and Conflicts",
    ];

    private static readonly Lazy<IReadOnlyList<Node>> Nodes = new(Read);

    [Fact]
    public void Seed_ShouldHoldTheThirtyFourDomainsByTheirIabLabels()
    {
        var domains = Nodes.Value.Where(node => node.Tier == 1).ToArray();

        domains.Select(node => node.Label).Should().BeEquivalentTo(Domains);
        domains.Should().OnlyContain(node => node.Parent == null && !node.Key.Contains('/'));
        domains.Single(node => node.Label == "Style & Fashion").Should().Be(new Node("style-fashion", "552", null, 1, "Style & Fashion", false));
    }

    [Fact]
    public void Seed_ShouldLeaveOutEventsGenresAndSensitiveTopics_WithTheirSubtrees()
    {
        Nodes.Value.Should().NotContain(node => node.Key == "events" || node.Key.StartsWith("events/", StringComparison.Ordinal)
            || node.Key == "genres" || node.Key.StartsWith("genres/", StringComparison.Ordinal)
            || node.Key == "sensitive-topics" || node.Key.StartsWith("sensitive-topics/", StringComparison.Ordinal));
        Nodes.Value.Should().NotContain(node => node.IabId == "8VZQHL" || node.IabId == "SPSHQ5" || node.IabId == "v9i3On");
    }

    [Fact]
    public void Seed_ShouldCountEveryDescendantOnce_AsATreeOfSlugPaths()
    {
        Nodes.Value.Should().HaveCount(657);
        Nodes.Value.GroupBy(node => node.Tier).ToDictionary(group => group.Key, group => group.Count())
            .Should().Equal(new Dictionary<int, int> { [1] = 34, [2] = 280, [3] = 274, [4] = 69 });
        Nodes.Value.Select(node => node.Key).Should().OnlyHaveUniqueItems();
        Nodes.Value.Select(node => node.IabId).Should().OnlyHaveUniqueItems();
        Nodes.Value.Should().OnlyContain(node => SlugPath().IsMatch(node.Key) && node.Key.Split('/').Length == node.Tier && !node.Extension);
        var keys = Nodes.Value.Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
        Nodes.Value.Where(node => node.Parent is not null).Should()
            .OnlyContain(node => keys.Contains(node.Parent!) && node.Key.StartsWith(node.Parent + "/", StringComparison.Ordinal));
        Nodes.Value.Should().Contain(node => node.Key == "sports/equine-sports/horse-racing" && node.Parent == "sports/equine-sports",
            "the tier path wins over 3.1's Parent column, which files Horse Racing under Sports");
    }

    [Fact]
    public void Migration_ShouldCarryTheIabAttribution()
    {
        Sql().Should().Contain("IAB Tech Lab Content Taxonomy 3.1, CC BY 3.0");
    }

    private static string Sql()
    {
        using var stream = typeof(HarvestModuleAssembly).Assembly.GetManifestResourceStream("Migrations/011-taxonomy/Apply.sql")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IReadOnlyList<Node> Read() =>
        [.. Row().Matches(Sql()).Select(match => new Node(match.Groups["key"].Value, match.Groups["iab"].Value,
            match.Groups["parent"].Success ? match.Groups["parent"].Value : null, int.Parse(match.Groups["tier"].Value),
            match.Groups["label"].Value.Replace("''", "'"), match.Groups["ext"].Value == "true"))];

    private sealed record Node(string Key, string IabId, string? Parent, int Tier, string Label, bool Extension);

    [GeneratedRegex(@"^\s+\('(?<key>[^']+)', '(?<iab>[^']+)', (?:'(?<parent>[^']+)'|NULL), (?<tier>\d), '(?<label>(?:[^']|'')+)', (?<ext>true|false), true\)", RegexOptions.Multiline)]
    private static partial Regex Row();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)*$")]
    private static partial Regex SlugPath();
}
