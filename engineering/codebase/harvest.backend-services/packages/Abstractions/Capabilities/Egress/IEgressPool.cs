using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Leases exits: the direct one, or proxies from vendor pools and operator-run IP servers.</summary>
/// <remarks>References only the BCL and the kernel, so it moves to the core <c>Http/Egress</c> module by a namespace
/// change. Leases are taken after a claim commits and before any I/O; an unavailable exit is a failure, never a wait.</remarks>
public interface IEgressPool
{
    /// <summary>Leases an exit for the request.</summary>
    /// <param name="request">The exit wanted.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The lease; <c>ExternalUnavailable</c> when no exit of the kind is free, <c>ExternalUnauthorized</c> when
    /// the vendor refuses the account.</returns>
    Task<Result<EgressLease>> LeaseAsync(EgressRequest request, CancellationToken cancellationToken = default);

    /// <summary>Releases a lease and records what it carried.</summary>
    /// <param name="lease">The lease to release.</param>
    /// <param name="usage">The requests and bytes the lease carried.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the lease is released.</returns>
    Task ReleaseAsync(EgressLease lease, EgressUsage usage, CancellationToken cancellationToken = default);

    /// <summary>Records how a host treated the lease's exit, so a blocked exit is not offered to that host again soon.</summary>
    /// <param name="lease">The lease whose exit was used.</param>
    /// <param name="host">The host that answered.</param>
    /// <param name="verdict">How the host treated the exit.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that completes when the verdict is recorded.</returns>
    Task ReportAsync(EgressLease lease, string host, EgressVerdict verdict, CancellationToken cancellationToken = default);
}
