using System.Collections.Concurrent;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Holds the <c>robots.txt</c> a host answered through one fixed route, for the lifetime the direct route uses.</summary>
/// <remarks>A route that is private (<c>IFetchTransportLease.IsPrivate</c>) keeps no robots in the host's durable state, which a
/// fetch through another route or a session must not read. A route whose exit does not change what a site's robots say
/// (<c>CachesRobotsByRoute</c>) keeps them here instead, by host and route key, so only its first fetch reads the file. The cache
/// is in memory: a restart reads robots once more. An entry is read and written only under its host's fetch lock.</remarks>
internal sealed class RouteRobotsCache
{
    private readonly ConcurrentDictionary<(string Host, string Route), HostFetchState> entries = new();

    /// <summary>Gets the robots slot of a host on a route; a new one holds nothing, so the caller reads the file.</summary>
    internal HostFetchState For(string host, string routeKey) => entries.GetOrAdd((host, routeKey), _ => new HostFetchState());
}
