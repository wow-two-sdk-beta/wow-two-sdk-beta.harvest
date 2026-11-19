using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Says which (entity type, domain) pairs some subscriber wants extracted: the demand gate of Layer 2.</summary>
/// <remarks>The module's default reads <c>Harvest:Processing:Extraction:Wanted</c>; the subscription selector replaces
/// it by registering its own implementation before <c>AddHarvestPostExtraction</c>. A post qualifies for a pair when its
/// done <c>post-tags</c> answer has the pair's entity type and a domain node at or under the pair's key.</remarks>
public interface IPostExtractionDemand
{
    /// <summary>Lists the pairs wanted now.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The wanted pairs; empty when nothing is wanted.</returns>
    ValueTask<IReadOnlyList<PostExtractionWant>> WantedAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents one wanted pair: an entity type and a taxonomy key prefix.</summary>
/// <remarks>Example: <c>event</c> with <c>style-fashion</c> for Ocharo, <c>listing</c> with <c>real-estate</c> for Haven.</remarks>
public sealed record PostExtractionWant
{
    /// <summary>Gets or sets the entity type: <c>event</c> or <c>listing</c>.</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Gets or sets the taxonomy key the post's domain must equal or sit under, such as <c>style-fashion</c>.</summary>
    public string Domain { get; set; } = "";
}

/// <summary>Matches a post's tags against the wanted pairs.</summary>
internal static partial class PostExtractionDemand
{
    /// <summary>Whether a pair is well formed: a known entity type and a taxonomy key.</summary>
    internal static bool IsValid(PostExtractionWant want) =>
        PostExtractionConstants.UseCaseFor(want.EntityType?.Trim().ToLowerInvariant()) is not null
        && want.Domain is not null && TaxonomyKey().IsMatch(want.Domain.Trim().ToLowerInvariant());

    /// <summary>Normalizes the pairs: lowercase, trimmed, valid and distinct.</summary>
    internal static IReadOnlyList<PostExtractionWant> Normalize(IEnumerable<PostExtractionWant> wanted) =>
        [.. wanted.Where(IsValid)
            .Select(want => new PostExtractionWant { EntityType = want.EntityType.Trim().ToLowerInvariant(), Domain = want.Domain.Trim().ToLowerInvariant() })
            .Distinct()];

    /// <summary>Whether <paramref name="node"/> is <paramref name="key"/> or sits under it: <c>style-fashion/womens-fashion</c>
    /// is under <c>style-fashion</c>; <c>style-fashion-x</c> is not.</summary>
    internal static bool AtOrUnder(string node, string key) =>
        node == key || node.Length > key.Length && node.StartsWith(key, StringComparison.Ordinal) && node[key.Length] == '/';

    /// <summary>Returns the use case a post's tags qualify for, or null: its entity type must be wanted with a domain
    /// that one of its nodes equals or sits under.</summary>
    /// <param name="wanted">The normalized wanted pairs.</param>
    /// <param name="entityType">The post's entity type.</param>
    /// <param name="domains">The post's taxonomy nodes.</param>
    internal static string? Match(IReadOnlyList<PostExtractionWant> wanted, string? entityType, IEnumerable<PostTagsDomain> domains) =>
        MatchPair(wanted, entityType, domains) is not null ? PostExtractionConstants.UseCaseFor(entityType) : null;

    /// <summary>Returns the first wanted pair a post's tags match, or null: its entity type, and a domain one of its
    /// nodes equals or sits under.</summary>
    /// <param name="wanted">The normalized wanted pairs.</param>
    /// <param name="entityType">The post's entity type.</param>
    /// <param name="domains">The post's taxonomy nodes.</param>
    internal static PostExtractionWant? MatchPair(IReadOnlyList<PostExtractionWant> wanted, string? entityType, IEnumerable<PostTagsDomain> domains)
    {
        if (PostExtractionConstants.UseCaseFor(entityType) is null)
            return null;
        var nodes = domains.Select(domain => domain.Node).ToArray();
        return wanted.FirstOrDefault(want => want.EntityType == entityType && nodes.Any(node => AtOrUnder(node, want.Domain)));
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TaxonomyKey();
}

/// <summary>Reads demand from <see cref="PostExtractionOptions.Wanted"/>; stands in until the selector supplies it.</summary>
internal sealed class ConfiguredPostExtractionDemand(IOptionsMonitor<PostExtractionOptions> options) : IPostExtractionDemand
{
    public ValueTask<IReadOnlyList<PostExtractionWant>> WantedAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(PostExtractionDemand.Normalize(options.CurrentValue.Wanted));
}

/// <summary>Answers whether this host extracts at all: it composes the shared AI runner and a demand source.</summary>
internal sealed class PostExtractionGate(IServiceProvider services, IServiceProviderIsService registered)
{
    /// <summary>Gets whether extraction can queue and run in this host.</summary>
    internal bool Enabled => registered.IsService(typeof(IAiRunner)) && registered.IsService(typeof(IPostExtractionDemand));

    /// <summary>Lists the normalized wanted pairs; empty when the host does not extract.</summary>
    internal async ValueTask<IReadOnlyList<PostExtractionWant>> WantedAsync(CancellationToken ct) =>
        Enabled && services.GetService<IPostExtractionDemand>() is { } demand
            ? PostExtractionDemand.Normalize(await demand.WantedAsync(ct))
            : [];
}
