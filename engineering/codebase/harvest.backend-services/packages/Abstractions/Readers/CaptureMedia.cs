namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Represents media a capture references, by its stable source URL.</summary>
public sealed record CaptureMedia
{
    /// <summary>Gets the media's URL, without expiring CDN tokens where the source allows.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the kind of media.</summary>
    public required MediaKind Kind { get; init; }

    /// <summary>Gets the media's position in the post's gallery.</summary>
    public int Position { get; init; }

    /// <summary>Gets the media type, when the source states it.</summary>
    public string? ContentType { get; init; }
}
