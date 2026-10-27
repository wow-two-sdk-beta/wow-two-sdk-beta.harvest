namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Represents what a detector reads of a response: status, headers and the start of the body.</summary>
public sealed record ChallengeProbe
{
    /// <summary>Gets the URL that was requested.</summary>
    public required Uri RequestUri { get; init; }

    /// <summary>Gets the URL that answered, after redirects.</summary>
    public required Uri FinalUri { get; init; }

    /// <summary>Gets the HTTP status.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Gets the media type of the body.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the response headers, in the order received.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the start of the decoded body, enough for markers.</summary>
    public string Body { get; init; } = string.Empty;
}
