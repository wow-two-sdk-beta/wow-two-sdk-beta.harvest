using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>One polite fetch of one URL: which host and lane it paces against, whether it obeys robots, the conditional
/// validators it carries, and the run's cookie jar.</summary>
internal sealed record FetchRequest
{
    /// <summary>Gets the URL to fetch.</summary>
    public required Uri Url { get; init; }

    /// <summary>Gets the host key the fetch paces against — the source's normalized host.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets the lane whose back-off state this fetch belongs to.</summary>
    public string Lane { get; init; } = "default";

    /// <summary>Gets whether the fetch obeys the host's <c>robots.txt</c>; the operator turns it off per source.</summary>
    public bool ObeyRobots { get; init; } = true;

    /// <summary>Gets the entity tag the last capture carried, sent as <c>If-None-Match</c>, or null.</summary>
    public string? ETag { get; init; }

    /// <summary>Gets when the resource last changed, sent as <c>If-Modified-Since</c>, or null.</summary>
    public DateTimeOffset? LastModifiedUtc { get; init; }

    /// <summary>Gets the step's own least gap between requests, folded into pacing, or null.</summary>
    public TimeSpan? StepMinDelay { get; init; }

    /// <summary>Gets the run's anonymous cookie jar, or null for none.</summary>
    public CookieContainer? Cookies { get; init; }
}
