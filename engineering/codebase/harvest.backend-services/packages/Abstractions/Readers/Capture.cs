using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Represents a reader's output: one post as seen at one source URL or id, with the bytes it came from.</summary>
/// <remarks>The store writes a capture row only when the hash of <see cref="ContentProjection"/> differs from the link's
/// latest; an unchanged poll touches the link's schedule only.</remarks>
public sealed record Capture
{
    /// <summary>Gets the link's identity within its source: the normalized URL on the web, <c>{peer}/{message id}</c> on
    /// Telegram, the ad id on a marketplace.</summary>
    public required string ExternalKey { get; init; }

    /// <summary>Gets the link's canonical URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the URL that was requested.</summary>
    public required string RequestUrl { get; init; }

    /// <summary>Gets the URL that answered, after redirects.</summary>
    public required string FinalUrl { get; init; }

    /// <summary>Gets the HTTP status, when the capture came from HTTP.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>Gets when the content was captured (UTC); the partition key of the capture store.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }

    /// <summary>Gets the media type of <see cref="Body"/>.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the body as received; its SHA-256 says whether the fetch was identical.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>Gets the normalized content projection — text, structured nodes, media by stable URL, the edited marker —
    /// without metrics, ads or session tokens; its SHA-256 says whether the post changed.</summary>
    public required string ContentProjection { get; init; }

    /// <summary>Gets the link's locale as BCP 47, with the script where it matters (<c>uz-Latn</c>, <c>uz-Cyrl</c>).</summary>
    public string? Locale { get; init; }

    /// <summary>Gets an exact cross-link key: a forward origin, a declared canonical URL, a JSON-LD <c>@id</c>.</summary>
    public string? OriginKey { get; init; }

    /// <summary>Gets how the link shows its post.</summary>
    public LinkRole Role { get; init; } = LinkRole.Primary;

    /// <summary>Gets the external key of the thread root for parts and replies.</summary>
    public string? ParentExternalKey { get; init; }

    /// <summary>Gets when the source says the post was published (UTC).</summary>
    public DateTimeOffset? PublishedAtUtc { get; init; }

    /// <summary>Gets the structured nodes the page carried, such as JSON-LD, as parsed JSON.</summary>
    public IReadOnlyList<JsonElement> Structured { get; init; } = [];

    /// <summary>Gets the media the post references.</summary>
    public IReadOnlyList<CaptureMedia> Media { get; init; } = [];

    /// <summary>Gets the links the post contains.</summary>
    public IReadOnlyList<CaptureLink> Links { get; init; } = [];

    /// <summary>Gets what the reader noticed but tolerated, such as a malformed node.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
