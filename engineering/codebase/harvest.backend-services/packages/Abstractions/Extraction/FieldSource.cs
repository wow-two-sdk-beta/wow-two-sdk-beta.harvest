namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents where one envelope field came from: the tier and the rule or parser.</summary>
public sealed record FieldSource
{
    /// <summary>Gets the tier that filled the field.</summary>
    public required ExtractionTier Tier { get; init; }

    /// <summary>Gets the rule or parser identifier.</summary>
    public string? Rule { get; init; }
}
