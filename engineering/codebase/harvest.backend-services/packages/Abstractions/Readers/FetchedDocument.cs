namespace WoW.Two.Sdk.Beta.Be.Harvest.Readers;

/// <summary>Represents a fetched response a reader turns into captures: the bytes as received, with their metadata.</summary>
public sealed record FetchedDocument
{
    /// <summary>Gets the URL that was requested.</summary>
    public required Uri RequestUrl { get; init; }

    /// <summary>Gets the URL that answered, after redirects.</summary>
    public required Uri FinalUrl { get; init; }

    /// <summary>Gets the HTTP status.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Gets the media type, such as <c>text/html</c> or <c>application/rss+xml</c>.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the character set the response declared.</summary>
    public string? Charset { get; init; }

    /// <summary>Gets the response headers, in the order received.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the body as received.</summary>
    public required ReadOnlyMemory<byte> Body { get; init; }

    /// <summary>Gets when the response arrived (UTC).</summary>
    public required DateTimeOffset FetchedAtUtc { get; init; }
}
