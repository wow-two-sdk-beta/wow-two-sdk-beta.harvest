using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

/// <summary>Leases sessions: anonymous cookie jars now, operator accounts and consumer connections later.</summary>
/// <remarks>Shaped as Social's session contract, so the implementation moves there without a change to callers.</remarks>
public interface ISessionLeases
{
    /// <summary>Leases a session for the request.</summary>
    /// <param name="request">The session wanted.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The lease; <c>ExternalUnavailable</c> when no session of the kind is free.</returns>
    Task<Result<SessionLease>> LeaseAsync(SessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stores the lease's current cookies and storage state, sealed, so a host-scoped jar stays warm.</summary>
    /// <param name="lease">The lease to store.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the state is stored.</returns>
    Task SaveAsync(SessionLease lease, CancellationToken cancellationToken = default);

    /// <summary>Releases a lease in the state it ends in.</summary>
    /// <param name="lease">The lease to release.</param>
    /// <param name="outcome">Whether the session still works.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the lease is released.</returns>
    Task ReleaseAsync(SessionLease lease, SessionOutcome outcome, CancellationToken cancellationToken = default);
}
