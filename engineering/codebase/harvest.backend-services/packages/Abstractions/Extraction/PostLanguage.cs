namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a post's language and script, by rules.</summary>
public sealed record PostLanguage
{
    /// <summary>Gets the language: <c>uz</c>, <c>ru</c>, <c>en</c>, <c>other</c> or <c>und</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the script: <c>Latn</c> or <c>Cyrl</c>.</summary>
    public string? Script { get; init; }

    /// <summary>Gets whether the text mixes languages.</summary>
    public bool Mixed { get; init; }

    /// <summary>Gets the confidence, 0–1.</summary>
    public double? Confidence { get; init; }
}
