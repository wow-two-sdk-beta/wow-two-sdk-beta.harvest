namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Represents a detected challenge: its kind, the detector that saw it and the marker that matched.</summary>
public sealed record ChallengeVerdict
{
    /// <summary>Gets the kind of check.</summary>
    public required ChallengeKind Kind { get; init; }

    /// <summary>Gets the detector that saw it.</summary>
    public required string DetectorKey { get; init; }

    /// <summary>Gets the marker that matched, for the evidence log.</summary>
    public required string Evidence { get; init; }
}
