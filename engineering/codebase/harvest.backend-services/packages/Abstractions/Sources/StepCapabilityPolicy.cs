namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents the operator's limits for one step of a source, layered over the adapter's defaults.</summary>
/// <remarks>Every member is optional: a null keeps the adapter's value.</remarks>
public sealed record StepCapabilityPolicy
{
    /// <summary>Gets the lane the step runs in, so its blocks never pause the host's other steps.</summary>
    public string? Lane { get; init; }

    /// <summary>Gets the step's monthly spending cap (USD).</summary>
    public decimal? MaxUsdPerMonth { get; init; }
}
