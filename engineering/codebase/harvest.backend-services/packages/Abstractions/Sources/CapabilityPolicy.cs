namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a source's capability policy: defaults for every step, per-step limits and a budget.</summary>
/// <remarks>Layered over the adapter's declared defaults and frozen into each run at queue time; a pipeline may lower it,
/// a subscriber never widens it.</remarks>
public sealed record CapabilityPolicy
{
    /// <summary>Gets the limits every step of the source starts from.</summary>
    public StepCapabilityPolicy? Defaults { get; init; }

    /// <summary>Gets the limits per step key, over <see cref="Defaults"/>.</summary>
    public IReadOnlyDictionary<string, StepCapabilityPolicy> Steps { get; init; } = new Dictionary<string, StepCapabilityPolicy>();

    /// <summary>Gets the source's monthly spending cap across paid work — rendering, providers, models (USD).</summary>
    public decimal? MonthUsd { get; init; }
}
