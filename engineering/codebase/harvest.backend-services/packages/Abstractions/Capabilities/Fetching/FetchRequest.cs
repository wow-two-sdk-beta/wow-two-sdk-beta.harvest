using System.Net;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Describes one direct page fetch and the source's politeness policy.</summary>
public sealed record FetchRequest
{
    /// <summary>Gets the absolute public HTTP or HTTPS URL to read.</summary>
    public required Uri Uri { get; init; }

    /// <summary>Gets whether the source obeys the host's robots policy.</summary>
    public RobotsPolicy RobotsPolicy { get; init; } = RobotsPolicy.Obey;

    /// <summary>Gets an additional minimum gap between requests; the longer host delay wins.</summary>
    public TimeSpan? MinDelay { get; init; }

    /// <summary>Gets the run's anonymous cookie jar, when one is used.</summary>
    public CookieContainer? Cookies { get; init; }

    /// <summary>Gets an optional source route restriction, applied before initial and redirected page requests.</summary>
    public IFetchDestinationPolicy? DestinationPolicy { get; init; }
}
