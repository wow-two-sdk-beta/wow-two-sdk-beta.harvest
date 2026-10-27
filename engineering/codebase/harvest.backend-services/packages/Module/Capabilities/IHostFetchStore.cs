namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Serializes a host's fetches and persists the state that survives worker restarts.</summary>
internal interface IHostFetchStore
{
    Task<IHostFetchLease> AcquireAsync(string host, CancellationToken cancellationToken);
}

/// <summary>Owns a host gate until its asynchronous disposal.</summary>
internal interface IHostFetchLease : IAsyncDisposable
{
    HostFetchState State { get; }
    Task SaveAsync(CancellationToken cancellationToken);
    Task<ResponseValidators?> GetValidatorsAsync(string url, CancellationToken cancellationToken);
    Task SaveValidatorsAsync(string url, ResponseValidators validators, CancellationToken cancellationToken);
}

/// <summary>Holds durable host policy and cached robots data.</summary>
internal sealed class HostFetchState
{
    internal DateTimeOffset NextAllowedAt { get; set; }
    internal string Permission { get; set; } = "open";
    internal string PolicyPermission { get; set; } = "open";
    internal DateTimeOffset? RetryAfterUntil { get; set; }
    internal DateTimeOffset? PausedUntil { get; set; }
    internal string? PauseReason { get; set; }
    internal int ConsecutiveFailures { get; set; }
    internal int MinimumDelayMilliseconds { get; set; } = 2000;
    internal int? CrawlDelaySeconds { get; set; }
    internal int AdaptiveDelayMilliseconds { get; set; } = 2000;
    internal string? RobotsBody { get; set; }
    internal string? RobotsOrigin { get; set; }
    internal int? RobotsStatus { get; set; }
    internal DateTimeOffset? RobotsCachedAt { get; set; }
}

/// <summary>Holds response validators without storing page content or credentials.</summary>
internal sealed record ResponseValidators(string? ETag, DateTimeOffset? LastModified, byte[]? Body = null, string? ContentType = null);
