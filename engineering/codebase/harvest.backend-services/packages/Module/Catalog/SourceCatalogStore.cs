using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;

/// <summary>Represents the instance's record of one catalog entry.</summary>
/// <param name="Key">The entry's key.</param>
/// <param name="TurnedOn">Whether the entry is on.</param>
/// <param name="ChangedAtUtc">When the record last changed (UTC).</param>
/// <param name="ChangedBy">Who changed it: an Identity subject, <c>operator-key</c>, <c>setup</c> or <c>existing</c>.</param>
public sealed record CatalogRecord(string Key, bool TurnedOn, DateTimeOffset ChangedAtUtc, string ChangedBy);

/// <summary>Reads and writes <c>harvest_catalog_sources</c> and the <c>catalog_key</c> of <c>harvest_sources</c>: the instance's choice
/// of which registered sources it uses.</summary>
/// <remarks>Every write that turns an entry on or off takes the scheduler's advisory lock, the one a source's master pause takes, so a
/// switch cannot race a run being queued.</remarks>
/// <param name="dataSource">The module's keyed data source.</param>
/// <param name="clock">The clock the records are stamped with.</param>
public sealed class SourceCatalogStore([FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource, TimeProvider clock)
    : ISourceCatalogRecords
{
    private const string SchedulerLock = "SELECT pg_advisory_xact_lock(hashtextextended('harvest:scheduler',0))";

    /// <summary>Reads every record by entry key.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<IReadOnlyDictionary<string, CatalogRecord>> ListRecordsAsync(CancellationToken cancellationToken = default)
    {
        var records = new Dictionary<string, CatalogRecord>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = PostSql.Command(connection, null, "SELECT key,turned_on,changed_at,changed_by FROM harvest_catalog_sources");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records[reader.GetString(0)] = new CatalogRecord(reader.GetString(0), reader.GetBoolean(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3));
        return records;
    }

    /// <summary>Reads the stored sources that carry an entry key, by key, across containers.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<CatalogSourceRef>>> StampedSourcesAsync(CancellationToken cancellationToken = default)
    {
        var stamped = new Dictionary<string, List<CatalogSourceRef>>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = PostSql.Command(connection, null, """
            SELECT s.catalog_key,s.id,s.container_id,COALESCE(s.display_name,s.handle)
            FROM harvest_sources s WHERE s.catalog_key IS NOT NULL ORDER BY s.catalog_key,s.family,s.handle,s.id
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!stamped.TryGetValue(reader.GetString(0), out var list))
                stamped[reader.GetString(0)] = list = [];
            list.Add(new CatalogSourceRef { SourceId = reader.GetGuid(1), ContainerId = reader.GetGuid(2), Name = reader.GetString(3) });
        }
        return stamped.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CatalogSourceRef>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Writes an entry's record, under the scheduler lock; the record keeps its time and author when the value is unchanged.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="turnedOn">Whether the entry is on.</param>
    /// <param name="changedBy">Who switches it.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task SetAsync(string key, bool turnedOn, string changedBy, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.ExecuteAsync(connection, transaction, SchedulerLock, cancellationToken);
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_catalog_sources(key,turned_on,changed_at,changed_by) VALUES(@key,@on,@now,@by)
            ON CONFLICT(key) DO UPDATE SET turned_on=EXCLUDED.turned_on,changed_at=EXCLUDED.changed_at,changed_by=EXCLUDED.changed_by
            WHERE harvest_catalog_sources.turned_on IS DISTINCT FROM EXCLUDED.turned_on
            """, cancellationToken, ("key", key), ("on", turnedOn), ("now", clock.GetUtcNow()), ("by", changedBy));
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Stamps an entry's key on the sources that carry none; a source already stamped keeps its key.</summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="sourceIds">The sources.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    public async Task StampAsync(string key, IReadOnlyCollection<Guid> sourceIds, CancellationToken cancellationToken = default)
    {
        if (sourceIds.Count == 0)
            return;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await PostSql.ExecuteAsync(connection, null, "UPDATE harvest_sources SET catalog_key=@key WHERE id=ANY(@ids) AND catalog_key IS NULL",
            cancellationToken, ("key", key), ("ids", sourceIds.ToArray()));
    }

    /// <summary>Writes an <c>on</c> record, changed by <c>existing</c>, for every stamped key that has none; an existing record never changes.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>How many records it wrote.</returns>
    public async Task<int> RecordExistingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await PostSql.ExecuteAsync(connection, null, """
            INSERT INTO harvest_catalog_sources(key,turned_on,changed_at,changed_by)
            SELECT DISTINCT s.catalog_key,true,@now,@by FROM harvest_sources s WHERE s.catalog_key IS NOT NULL
            ON CONFLICT(key) DO NOTHING
            """, cancellationToken, ("now", clock.GetUtcNow()), ("by", SourceCatalogConstants.ChangedByExisting));
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> OffKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = PostSql.Command(connection, null, "SELECT key FROM harvest_catalog_sources WHERE NOT turned_on");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            keys.Add(reader.GetString(0));
        return keys;
    }

    /// <inheritdoc />
    public async Task RecordSetUpAsync(string entryKey, IReadOnlyCollection<Guid> sourceIds, bool overrideOff, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.ExecuteAsync(connection, transaction, SchedulerLock, cancellationToken);
        if (sourceIds.Count > 0)
            await PostSql.ExecuteAsync(connection, transaction, "UPDATE harvest_sources SET catalog_key=@key WHERE id=ANY(@ids) AND catalog_key IS NULL",
                cancellationToken, ("key", entryKey), ("ids", sourceIds.ToArray()));
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_catalog_sources(key,turned_on,changed_at,changed_by) VALUES(@key,true,@now,@by)
            ON CONFLICT(key) DO UPDATE SET turned_on=true,changed_at=EXCLUDED.changed_at,changed_by=EXCLUDED.changed_by
            WHERE @override AND NOT harvest_catalog_sources.turned_on
            """, cancellationToken, ("key", entryKey), ("now", clock.GetUtcNow()), ("by", SourceCatalogConstants.ChangedBySetup), ("override", overrideOff));
        await transaction.CommitAsync(cancellationToken);
    }
}
