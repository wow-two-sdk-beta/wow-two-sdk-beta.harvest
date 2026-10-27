using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

/// <summary>Represents a leased session: its cookies and browser storage state, held in memory while leased and sealed
/// at rest.</summary>
public sealed record SessionLease
{
    /// <summary>Gets the lease's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets whose session it is.</summary>
    public required SessionKind Kind { get; init; }

    /// <summary>Gets the host the session is for.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets how long an anonymous session lives.</summary>
    public required SessionScope Scope { get; init; }

    /// <summary>Gets the session's cookie jar; requests and renders add to it.</summary>
    public required CookieContainer Cookies { get; init; }

    /// <summary>Gets the browser storage state a render starts from, as JSON; null before the first render.</summary>
    public string? StorageState { get; init; }

    /// <summary>Gets the account the session belongs to, for account and connection sessions.</summary>
    public string? AccountRef { get; init; }

    /// <summary>Gets the sticky key of the exit the account is pinned to, so its session never changes address.</summary>
    public string? PinnedEgressKey { get; init; }

    /// <summary>Gets when the lease lapses (UTC).</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
