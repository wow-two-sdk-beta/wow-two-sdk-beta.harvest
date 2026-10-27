using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents media in a post: the source URL always, the stored file once fetched.</summary>
public sealed record PostMedia
{
    /// <summary>Gets the kind of media.</summary>
    public required MediaKind Kind { get; init; }

    /// <summary>Gets the source URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the stored file's reference, once fetched into file storage.</summary>
    public string? FileRef { get; init; }

    /// <summary>Gets the content hash of the stored file.</summary>
    public string? Hash { get; init; }

    /// <summary>Gets the width in pixels.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the height in pixels.</summary>
    public int? Height { get; init; }

    /// <summary>Gets the media's role, such as <c>cover</c>.</summary>
    public string? Role { get; init; }
}
