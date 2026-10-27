namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what a step spent, attributed to its run, pipeline, tenant and subscription.</summary>
public sealed record StepUsage
{
    /// <summary>Gets the requests the step sent.</summary>
    public long Requests { get; init; }

    /// <summary>Gets the bytes the step read.</summary>
    public long Bytes { get; init; }

    /// <summary>Gets the step's estimated cost (USD).</summary>
    public decimal EstimatedUsd { get; init; }
}
