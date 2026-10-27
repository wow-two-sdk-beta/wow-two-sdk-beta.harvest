namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a tag from the shared vocabulary, with the tier that set it.</summary>
public sealed record PostTag
{
    /// <summary>Gets the tag key, such as <c>real-estate.rent</c>.</summary>
    public required string Tag { get; init; }

    /// <summary>Gets the score, 0–1.</summary>
    public double? Score { get; init; }

    /// <summary>Gets the tier that set the tag.</summary>
    public required ExtractionTier Tier { get; init; }

    /// <summary>Gets the version of the rule or model that set it.</summary>
    public string? Version { get; init; }
}
