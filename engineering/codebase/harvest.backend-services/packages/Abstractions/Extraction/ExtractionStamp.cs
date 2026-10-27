namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents what extraction ran, with versions, so a later version can re-extract.</summary>
public sealed record ExtractionStamp
{
    /// <summary>Gets the normalization version, such as <c>norm@1</c>.</summary>
    public required string NormVersion { get; init; }

    /// <summary>Gets the rules version.</summary>
    public required string RulesVersion { get; init; }

    /// <summary>Gets the tiers that ran, in order.</summary>
    public IReadOnlyList<ExtractionTier> TiersRun { get; init; } = [];

    /// <summary>Gets the model call, when a model tier ran.</summary>
    public AiStamp? Ai { get; init; }
}
