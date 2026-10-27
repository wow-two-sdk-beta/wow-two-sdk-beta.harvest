namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents a pipeline's change to one step of its adapter's plan: on or off, a lane or a stricter cap.</summary>
/// <remarks>The engine keeps the result inside the source's policy, so an override narrows and never widens.</remarks>
public sealed record StepOverride
{
    /// <summary>Gets whether an optional step runs; the adapter's default when null.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Gets the lane the step runs in.</summary>
    public string? Lane { get; init; }

    /// <summary>Gets the step's monthly spending cap in this pipeline (USD).</summary>
    public decimal? MaxUsdPerMonth { get; init; }
}
