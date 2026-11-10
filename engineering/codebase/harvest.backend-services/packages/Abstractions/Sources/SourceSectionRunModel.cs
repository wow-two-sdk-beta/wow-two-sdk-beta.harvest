using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a section's newest run of any work kind.</summary>
public sealed record SourceSectionRunModel
{
    /// <summary>Gets the run's identifier.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the work the run did.</summary>
    public required WorkKind Kind { get; init; }

    /// <summary>Gets when the run finished, else when it was queued (UTC).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets where the run stands.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Gets why the run stopped, as the store names a <see cref="RunReason"/> (<c>page_limit</c>).</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the captures the run changed or created.</summary>
    public required int NewPosts { get; init; }

    /// <summary>Gets the addresses the run discovered.</summary>
    public required int Found { get; init; }
}
