namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents the most one success may cost on a rung; a rung above its cap is skipped.</summary>
public sealed record RungCostCap
{
    /// <summary>Gets the rung, one of <see cref="Capabilities.CapabilityRungs"/>.</summary>
    public required string Rung { get; init; }

    /// <summary>Gets the most one success may cost on the rung (USD).</summary>
    public required decimal MaxUsdPerSuccess { get; init; }
}
