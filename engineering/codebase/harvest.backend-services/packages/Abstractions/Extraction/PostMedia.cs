using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents media in a post: the source URL always, the stored file once fetched.</summary>
public sealed record PostMedia
{
    /// <summary>Gets the kind of media.</summary>
    public required MediaKind Kind { get; init; }

    /// <summary>Gets the source URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the stored copy's file id (a GUID in <c>D</c> form), once SCFD copied the image into file storage. A post's
    /// own envelope leaves it null; the <c>wow2.event.v1</c> pull fills it on the wire from the copy the source-asset worker
    /// made, beside the source <see cref="Url"/>, which stays the credit link.</summary>
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
