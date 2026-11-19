using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_sources</c>.</summary>
internal sealed class SourceRow
{
    public const string TableName = "harvest_sources";

    public required Guid Id { get; set; }

    public required Guid ContainerId { get; set; }

    public required SourceFamily Family { get; set; }

    public required string Handle { get; set; }

    public string? ExternalRef { get; set; }

    /// <summary>Gets or sets the display name; null shows the handle.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the group in the same container.</summary>
    public Guid? GroupId { get; set; }

    public required SourceCoverage Coverage { get; set; }

    public required SourceCompleteness Completeness { get; set; }

    public required SourcePosture Posture { get; set; }

    public required SourcePermission Permission { get; set; }

    /// <summary>Gets or sets the older switch, kept in step for code before migration 016: true only for
    /// <see cref="ModelProcessingMode.Auto"/>, so older code never queues a manual source.</summary>
    public required bool AiAllowed { get; set; }

    /// <summary>Gets or sets how a model may process the source's posts (016).</summary>
    public required ModelProcessingMode ModelProcessing { get; set; }

    /// <summary>Sets <see cref="ModelProcessing"/> and keeps <see cref="AiAllowed"/> in step.</summary>
    public void SetModelProcessing(ModelProcessingMode mode)
    {
        ModelProcessing = mode;
        AiAllowed = mode == ModelProcessingMode.Auto;
    }

    public required bool PoolShareable { get; set; }

    public required RobotsPolicy RobotsPolicy { get; set; }

    public required ConsentBasis ConsentBasis { get; set; }

    public required short Authority { get; set; }

    public required string RetentionClass { get; set; }

    public Guid? CheckPolicyId { get; set; }

    /// <summary>Gets or sets the jsonb capability policy.</summary>
    public required string CapabilityPolicy { get; set; }

    public required SourceState State { get; set; }

    public required DateTimeOffset StateSince { get; set; }

    public string? CreatedBy { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
