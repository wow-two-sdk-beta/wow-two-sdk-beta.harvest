namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a registered source: where posts come from, with its posture switches and capability policy.</summary>
public sealed record SourceModel
{
    /// <summary>Gets the source's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the container the source belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the kind of origin the source is.</summary>
    public required SourceFamily Family { get; init; }

    /// <summary>Gets the source's normalized identity.</summary>
    public required string Handle { get; init; }

    /// <summary>Gets the platform's own identifier.</summary>
    public string? ExternalRef { get; init; }

    /// <summary>Gets how much of the source its pipelines see.</summary>
    public required SourceCoverage Coverage { get; init; }

    /// <summary>Gets whether the source's enumeration can prove absence.</summary>
    public required SourceCompleteness Completeness { get; init; }

    /// <summary>Gets the route class the source is read through.</summary>
    public required SourcePosture Posture { get; init; }

    /// <summary>Gets the operator's harvesting switch.</summary>
    public required SourcePermission Permission { get; init; }

    /// <summary>Gets whether model tiers and embeddings may read the source's text.</summary>
    public required bool AiAllowed { get; init; }

    /// <summary>Gets whether posts read from the source may serve every tenant from the pool.</summary>
    public required bool PoolShareable { get; init; }

    /// <summary>Gets whether fetches obey the host's <c>robots.txt</c>.</summary>
    public required RobotsPolicy RobotsPolicy { get; init; }

    /// <summary>Gets who consented to the source's content reaching Harvest.</summary>
    public required ConsentBasis ConsentBasis { get; init; }

    /// <summary>Gets how close the source is to the thing it reports, 0–100.</summary>
    public required int Authority { get; init; }

    /// <summary>Gets the retention class.</summary>
    public required string RetentionClass { get; init; }

    /// <summary>Gets the deletion-check policy key; the family's default when null.</summary>
    public string? CheckPolicy { get; init; }

    /// <summary>Gets the operator's capability limits for the source's steps.</summary>
    public required CapabilityPolicy CapabilityPolicy { get; init; }

    /// <summary>Gets the source's availability.</summary>
    public required SourceState State { get; init; }

    /// <summary>Gets when the source entered its state (UTC).</summary>
    public required DateTimeOffset StateSinceUtc { get; init; }

    /// <summary>Gets when the source was registered (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
