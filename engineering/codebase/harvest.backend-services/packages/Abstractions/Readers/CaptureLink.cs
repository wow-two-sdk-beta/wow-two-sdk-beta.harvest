namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Represents a link a capture contains, such as a ticket or a social profile.</summary>
public sealed record CaptureLink
{
    /// <summary>Gets the link's absolute URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the link's text.</summary>
    public string? Text { get; init; }

    /// <summary>Gets the link's relation, such as <c>canonical</c> or <c>alternate</c>.</summary>
    public string? Rel { get; init; }
}
