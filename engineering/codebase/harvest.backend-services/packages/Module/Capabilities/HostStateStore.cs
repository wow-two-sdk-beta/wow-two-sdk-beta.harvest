using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Reads and writes a host's pacing and a lane's back-off over <c>harvest_host_state</c> and
/// <c>harvest_host_lane</c>, the tables the Fetching lane owns.</summary>
/// <remarks>Rows are tracked so a fetch's decision writes straight back. The engine's host gate serializes workers on one
/// host; this does not claim or lock on its own.</remarks>
internal sealed class HostStateStore(HarvestDbContext db)
{
    /// <summary>Loads the host's row, creating a polite default when the host is new; the row is tracked.</summary>
    /// <param name="hostKey">The host's key.</param>
    /// <param name="now">The clock, stamped on a new row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<HostStateRow> HostAsync(string hostKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.HostStates.FirstOrDefaultAsync(state => state.HostKey == hostKey, cancellationToken);
        if (row is null)
        {
            row = new HostStateRow { HostKey = hostKey, NextAllowedAt = now, UpdatedAt = now };
            db.HostStates.Add(row);
        }

        return row;
    }

    /// <summary>Loads the host and lane's row, creating one when the lane is new; the row is tracked.</summary>
    /// <param name="hostKey">The host's key.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="now">The clock, stamped on a new row.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async Task<HostLaneRow> LaneAsync(string hostKey, string lane, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.HostLanes.FirstOrDefaultAsync(entry => entry.HostKey == hostKey && entry.Lane == lane, cancellationToken);
        if (row is null)
        {
            row = new HostLaneRow { HostKey = hostKey, Lane = lane, UpdatedAt = now };
            db.HostLanes.Add(row);
        }

        return row;
    }

    /// <summary>Reads the host's row without tracking it, or null when the host is new.</summary>
    /// <param name="hostKey">The host's key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<HostStateRow?> PeekHostAsync(string hostKey, CancellationToken cancellationToken) =>
        db.HostStates.AsNoTracking().FirstOrDefaultAsync(state => state.HostKey == hostKey, cancellationToken);

    /// <summary>Reads the host and lane's row without tracking it, or null when the lane is new.</summary>
    /// <param name="hostKey">The host's key.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task<HostLaneRow?> PeekLaneAsync(string hostKey, string lane, CancellationToken cancellationToken) =>
        db.HostLanes.AsNoTracking().FirstOrDefaultAsync(entry => entry.HostKey == hostKey && entry.Lane == lane, cancellationToken);

    /// <summary>Writes the tracked changes.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
