using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents an operator's existence observation over the HTTP form.</summary>
public sealed record LinkObservationRequest
{
    /// <summary>Gets the owning container.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the registered source.</summary>
    public required Guid SourceId { get; init; }
    /// <summary>Gets the run that made the observation, when one exists.</summary>
    public Guid? RunId { get; init; }
    /// <summary>Gets the link evidence.</summary>
    public required LinkObservation Observation { get; init; }
}
