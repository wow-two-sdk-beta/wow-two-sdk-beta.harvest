namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Reads another eligible professional publisher through Meta's official Business Discovery API.</summary>
public interface IInstagramMediaClient
{
    /// <summary>Reads a bounded recent-media page; configuration, permission and throttling failures are explicit outcomes.</summary>
    /// <param name="username">An allowlisted publisher username.</param>
    /// <param name="after">An opaque cursor from an earlier response.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<InstagramMediaPage> ReadAsync(string username, string? after = null, CancellationToken cancellationToken = default);
}
