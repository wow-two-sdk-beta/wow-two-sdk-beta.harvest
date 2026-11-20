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

    /// <summary>Gets the display name the console shows; null shows the handle.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the source's group in its container; null when ungrouped.</summary>
    public Guid? GroupId { get; init; }

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

    /// <summary>Gets whether a model may read the source's text at all: <see cref="ModelProcessing"/> is not
    /// <see cref="ModelProcessingMode.Off"/>. Kept for older callers and stored runs: setting it maps <c>true</c> to
    /// <see cref="ModelProcessingMode.Auto"/> and <c>false</c> to <see cref="ModelProcessingMode.Off"/>, unless
    /// <see cref="ModelProcessing"/> already agrees.</summary>
    public bool AiAllowed
    {
        get => ModelProcessing != ModelProcessingMode.Off;
        init
        {
            if (value != AiAllowed)
                ModelProcessing = value ? ModelProcessingMode.Auto : ModelProcessingMode.Off;
        }
    }

    /// <summary>Gets how a model may process the source's posts: never, on operator runs only, or automatically.</summary>
    public ModelProcessingMode ModelProcessing { get; init; }

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

    /// <summary>Gets the catalog entry the source was set up for (<c>ocharo.afisha</c>); null for a source registered by hand,
    /// which stays outside the catalog.</summary>
    public string? CatalogKey { get; init; }

    /// <summary>Gets whether the instance keeps the source's catalog entry turned on: true with no <see cref="CatalogKey"/> or
    /// an entry that is not off. A source that is not turned on never schedules, captures, is processed by a model or delivers.</summary>
    public bool TurnedOn { get; init; } = true;

    /// <summary>Gets the source's availability.</summary>
    public required SourceState State { get; init; }

    /// <summary>Gets when the source entered its state (UTC).</summary>
    public required DateTimeOffset StateSinceUtc { get; init; }

    /// <summary>Gets when the source was registered (UTC).</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}
