using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a source's newest collection or processing run across its sections.</summary>
public sealed record SourceHarvestModel
{
    /// <summary>Gets the run's identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the section's pipeline that ran it.</summary>
    public required Guid PipelineId { get; init; }

    /// <summary>Gets which collection or processing stage ran.</summary>
    public WorkKind Kind { get; init; } = WorkKind.Capture;

    /// <summary>Gets when the run finished, else when it was queued (UTC).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets where the run stands.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Gets why the run stopped, as the store names a <see cref="RunReason"/> (<c>page_limit</c>).</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the posts the run created, falling back to changed captures for older runs.</summary>
    public required int NewPosts { get; init; }

    /// <summary>Gets the addresses the run discovered.</summary>
    public required int Found { get; init; }
}
