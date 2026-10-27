namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a request to register a source in a container.</summary>
/// <remarks>The posture switches ship permissive; a ban or a takedown later changes one value, never the schema.</remarks>
public sealed record SourceRegistration
{
    /// <summary>Gets the container the source belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the kind of origin the source is.</summary>
    public required SourceFamily Family { get; init; }

    /// <summary>Gets the source's identity: a host for web families (a URL is reduced to its host), <c>tg:{username}</c>
    /// for Telegram; normalized on save and unique per container and family.</summary>
    public required string Handle { get; init; }

    /// <summary>Gets the platform's own identifier, such as a Telegram peer id; it wins over the handle.</summary>
    public string? ExternalRef { get; init; }

    /// <summary>Gets how much of the source its pipelines see.</summary>
    public required SourceCoverage Coverage { get; init; }

    /// <summary>Gets whether the source's enumeration can prove absence.</summary>
    public required SourceCompleteness Completeness { get; init; }

    /// <summary>Gets the route class the source is read through; required.</summary>
    public required SourcePosture Posture { get; init; }

    /// <summary>Gets the operator's harvesting switch.</summary>
    public SourcePermission Permission { get; init; } = SourcePermission.Open;

    /// <summary>Gets whether model tiers and embeddings may read the source's text.</summary>
    public bool AiAllowed { get; init; } = true;

    /// <summary>Gets whether posts read from the source may serve every tenant from the pool.</summary>
    public bool PoolShareable { get; init; } = true;

    /// <summary>Gets whether fetches obey the host's <c>robots.txt</c>.</summary>
    public RobotsPolicy RobotsPolicy { get; init; } = RobotsPolicy.Obey;

    /// <summary>Gets who consented to the source's content reaching Harvest.</summary>
    public ConsentBasis ConsentBasis { get; init; } = ConsentBasis.None;

    /// <summary>Gets how close the source is to the thing it reports, 0–100 (organizer 80, aggregator 50, repost 20).</summary>
    public int Authority { get; init; } = SourceConstants.DefaultAuthority;

    /// <summary>Gets the retention class, one of <see cref="SourceConstants.RetentionClasses"/>.</summary>
    public string RetentionClass { get; init; } = SourceConstants.StandardRetentionClass;

    /// <summary>Gets the deletion-check policy key, one of <see cref="SourceConstants.CheckPolicies"/>; the family's default
    /// when null.</summary>
    public string? CheckPolicy { get; init; }

    /// <summary>Gets the operator's capability limits for the source's steps.</summary>
    public CapabilityPolicy? CapabilityPolicy { get; init; }
}
