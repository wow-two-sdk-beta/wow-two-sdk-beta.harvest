using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents one claimed unit of work a step broker executes.</summary>
public sealed record StepItem
{
    /// <summary>Gets the work item's identifier.</summary>
    public required Guid WorkItemId { get; init; }

    /// <summary>Gets the run the item belongs to.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the pipeline the run belongs to.</summary>
    public required Guid PipelineId { get; init; }

    /// <summary>Gets the container the run harvests into.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the source the run reads.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the step to execute.</summary>
    public required string StepKey { get; init; }

    /// <summary>Gets the lane the item was claimed in.</summary>
    public required string Lane { get; init; }

    /// <summary>Gets the host the item was claimed for.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets the item's target: a URL, or a step-specific key.</summary>
    public required string Target { get; init; }

    /// <summary>Gets step-specific data the previous step left, such as a page cursor.</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>Gets how many times the item was claimed before, for backoff.</summary>
    public int Attempt { get; init; }
}
