namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Profiles;

/// <summary>Represents a named, coherent header set: the user agent, client hints and language that agree.</summary>
/// <remarks>One profile per egress session; headers only, so a host that fingerprints TLS needs the render rung.</remarks>
public sealed record RequestProfile
{
    /// <summary>Gets the profile's name, such as <c>bot</c> or <c>browser:chromium-desktop</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the user agent.</summary>
    public required string UserAgent { get; init; }

    /// <summary>Gets the other headers, in the order they are sent.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];
}
