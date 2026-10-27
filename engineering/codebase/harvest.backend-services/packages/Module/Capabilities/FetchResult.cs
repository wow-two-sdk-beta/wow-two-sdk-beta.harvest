using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>What one polite fetch came back as: the outcome the run records, the document on success, the validators to
/// carry next time, and the pacing the host now holds.</summary>
/// <remarks>A challenge, a block or a rate limit is here as a recorded outcome with its evidence; nothing retried past
/// it.</remarks>
internal sealed record FetchResult
{
    /// <summary>Gets what the fetch came back as.</summary>
    public required FetchOutcome Outcome { get; init; }

    /// <summary>Gets the HTTP status the host answered, or null when none was reached.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Gets the fetched document, on <see cref="FetchOutcome.Ok"/>.</summary>
    public FetchedDocument? Document { get; init; }

    /// <summary>Gets the entity tag the response carried, to send next time.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets when the response said the resource last changed, to send next time.</summary>
    public DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Gets how long the host asked to wait, from <c>Retry-After</c>.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Gets the challenge verdict, when a challenge, a block or a rate limit stood in the way.</summary>
    public ChallengeVerdict? Challenge { get; init; }

    /// <summary>Gets the marker or reason recorded with the outcome.</summary>
    public string? Evidence { get; init; }

    /// <summary>Gets when the host may be fetched again.</summary>
    public required DateTimeOffset NextAllowedAtUtc { get; init; }

    /// <summary>Gets when the lane's pause lifts, or null when it is not paused.</summary>
    public DateTimeOffset? PausedUntilUtc { get; init; }
}

/// <summary>Whether a host may be fetched now, and when it becomes fetchable if not.</summary>
/// <param name="Allowed">Whether the host is fetchable now.</param>
/// <param name="DueAtUtc">When the host becomes fetchable.</param>
/// <param name="Wait">How long until then, zero when allowed.</param>
internal sealed record FetchGate(bool Allowed, DateTimeOffset DueAtUtc, TimeSpan Wait);

/// <summary>The polite fetcher the Engine drives: it paces each host, obeys robots, sends conditional requests, honours
/// <c>Retry-After</c>, classifies the outcome and records the host's back-off.</summary>
internal interface IPoliteFetcher
{
    /// <summary>Fetches a URL politely and records the outcome against the host and lane.</summary>
    /// <param name="request">The fetch to make.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reports whether a host and lane may be fetched now, without fetching.</summary>
    /// <param name="hostKey">The host key.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task<FetchGate> ReserveAsync(string hostKey, string lane = "default", CancellationToken cancellationToken = default);
}
