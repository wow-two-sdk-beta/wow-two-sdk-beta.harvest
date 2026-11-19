using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Drives Layer 2 extraction demand from the active selector subscriptions: the pairs the configured list names,
/// plus an (entity type, domain key) pair for each domain include of each active selector.</summary>
/// <remarks>
///   - <c>except</c> lists and topics add nothing: <see cref="PostExtractionWant"/> has no topic field, and a post under
///     an excepted node is still extracted, so the answer is ready the day the exception is removed
///   - the selectors are read at most every ten seconds; a selector change calls <see cref="Invalidate"/> so its own
///     extraction backfill sees the new pairs at once
///   - the configured list keeps serving pairs no selector names, such as another product's listing pair
/// </remarks>
public sealed class SelectorPostExtractionDemand(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    IOptionsMonitor<PostExtractionOptions> options,
    TimeProvider time) : IPostExtractionDemand
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);

    private readonly object gate = new();
    private Snapshot? snapshot;
    private int generation;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<PostExtractionWant>> WantedAsync(CancellationToken cancellationToken = default)
    {
        var selectorPairs = await SelectorPairsAsync(cancellationToken);
        return PostExtractionDemand.Normalize([.. options.CurrentValue.Wanted, .. selectorPairs]);
    }

    /// <summary>Forgets the cached selector pairs, so the next call reads the active selectors again.</summary>
    public void Invalidate()
    {
        lock (gate)
        {
            generation++;
            snapshot = null;
        }
    }

    private async ValueTask<IReadOnlyList<PostExtractionWant>> SelectorPairsAsync(CancellationToken ct)
    {
        int seen;
        lock (gate)
        {
            if (snapshot is { } cached && time.GetUtcNow() < cached.ExpiresAt)
                return cached.Pairs;
            seen = generation;
        }

        var pairs = new List<PostExtractionWant>();
        await using (var command = dataSource.CreateCommand("SELECT selector::text FROM harvest_subscriptions WHERE kind='selector' AND state='active' ORDER BY created_at,id"))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                foreach (var (entityType, domain) in SelectorRules.Pairs(SelectorStore.ReadSelector(reader.GetString(0))))
                    pairs.Add(new PostExtractionWant { EntityType = entityType, Domain = domain });
        var read = (IReadOnlyList<PostExtractionWant>)[.. pairs.Distinct()];

        lock (gate)
        {
            // A change that landed while the read ran leaves the cache empty: this answer may predate it.
            if (generation == seen)
                snapshot = new Snapshot(read, time.GetUtcNow().Add(CacheFor));
        }
        return read;
    }

    private sealed record Snapshot(IReadOnlyList<PostExtractionWant> Pairs, DateTimeOffset ExpiresAt);
}
