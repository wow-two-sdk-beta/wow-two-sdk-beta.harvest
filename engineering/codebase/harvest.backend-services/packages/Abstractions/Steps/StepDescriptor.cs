namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents one step an adapter declares: its key, role, requirements and lane.</summary>
public sealed record StepDescriptor
{
    /// <summary>Gets the step's key, unique within the adapter, matching <see cref="StepConstants.KeyPattern"/>.</summary>
    public required string Key { get; init; }

    /// <summary>Gets what the step does.</summary>
    public required StepRole Role { get; init; }

    /// <summary>Gets the capabilities the step needs.</summary>
    public StepRequirements Requires { get; init; } = new();

    /// <summary>Gets the lane the step runs in, so a block in it never pauses the host's other steps.</summary>
    public string Lane { get; init; } = StepConstants.DefaultLane;

    /// <summary>Gets whether a pipeline may turn the step off; optional steps run only when enabled.</summary>
    public bool Optional { get; init; }
}
