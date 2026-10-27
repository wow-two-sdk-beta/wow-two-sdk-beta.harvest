using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents follow-up work a step enqueues for a later step of the same run — the frontier.</summary>
public sealed record StepTarget
{
    /// <summary>Gets the step that will execute the target.</summary>
    public required string StepKey { get; init; }

    /// <summary>Gets the target: a URL, or a step-specific key; unique per run and step.</summary>
    public required string Target { get; init; }

    /// <summary>Gets step-specific data for the next step, such as a page cursor.</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>Gets the target's priority; the run's when null.</summary>
    public WorkPriority? Priority { get; init; }
}
