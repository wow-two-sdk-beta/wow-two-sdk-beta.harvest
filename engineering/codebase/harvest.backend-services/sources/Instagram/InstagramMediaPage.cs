using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>A bounded page or an explicit platform outcome; never contains an access token or a provider error body.</summary>
public sealed record InstagramMediaPage
{
    /// <summary>Gets the platform outcome.</summary>
    public required FetchOutcome Outcome { get; init; }
    /// <summary>Gets the returned media records.</summary>
    public IReadOnlyList<JsonElement> Media { get; init; } = [];
    /// <summary>Gets the opaque cursor, extracted independently of the provider's next URL.</summary>
    public string? After { get; init; }
    /// <summary>Gets the actual credential-free Graph request URL for transport provenance.</summary>
    public string? RequestUrl { get; init; }
    /// <summary>Gets the actual HTTP status, absent for a fixture or an unavailable capability.</summary>
    public int? HttpStatus { get; init; }
    /// <summary>Gets actual request and byte usage.</summary>
    public StepUsage Usage { get; init; } = new();
    /// <summary>Gets the requested backoff.</summary>
    public TimeSpan? RetryAfter { get; init; }
    /// <summary>Gets an actionable message without provider-supplied text or secrets.</summary>
    public string? Message { get; init; }
}
