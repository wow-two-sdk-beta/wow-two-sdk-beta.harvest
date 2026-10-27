namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a link a post contains.</summary>
public sealed record PostLink
{
    /// <summary>Gets the absolute URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets what the link leads to.</summary>
    public required PostLinkKind Kind { get; init; }

    /// <summary>Gets the link's text.</summary>
    public string? Text { get; init; }
}
