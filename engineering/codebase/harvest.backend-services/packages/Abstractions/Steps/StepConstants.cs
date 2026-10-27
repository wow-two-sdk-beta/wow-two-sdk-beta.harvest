namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Holds the shape of step keys and lanes.</summary>
public static class StepConstants
{
    /// <summary>Holds the lane a step runs in unless it declares one.</summary>
    public const string DefaultLane = "default";

    /// <summary>Holds the pattern of step keys and lanes: a lowercase letter, then lowercase letters, digits and hyphens.</summary>
    public const string KeyPattern = "^[a-z][a-z0-9-]{0,39}$";

    /// <summary>Holds the longest step key or lane.</summary>
    public const int MaxKeyLength = 40;
}
