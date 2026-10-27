namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what the engine resolved for one claim: the step, its lane and host, and its requirements.</summary>
public sealed record ResolvedPlan
{
    /// <summary>Gets the step being executed.</summary>
    public required string StepKey { get; init; }

    /// <summary>Gets the lane the item was claimed in.</summary>
    public required string Lane { get; init; }

    /// <summary>Gets the host the item was claimed for.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets the requirements after the adapter, source and pipeline layers.</summary>
    public required StepRequirements Requirements { get; init; }
}
