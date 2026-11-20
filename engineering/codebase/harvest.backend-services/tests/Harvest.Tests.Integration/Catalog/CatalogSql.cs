using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>Writes the instance's catalog state straight into the tables, so a gate is tested apart from the broker that normally
/// writes it.</summary>
internal static class CatalogSql
{
    internal const string Key = "test.entry";

    internal static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Links a source to the entry and records the entry as off.</summary>
    internal static async Task TurnOffAsync(string connectionString, Guid sourceId, string key = Key)
    {
        await ExecuteAsync(connectionString, "UPDATE harvest_sources SET catalog_key=@key WHERE id=@id", ("key", key), ("id", sourceId));
        await SetAsync(connectionString, key, false);
    }

    /// <summary>Writes the entry's record.</summary>
    internal static Task SetAsync(string connectionString, string key, bool turnedOn) => ExecuteAsync(connectionString, """
        INSERT INTO harvest_catalog_sources(key,turned_on,changed_by) VALUES(@key,@on,'test')
        ON CONFLICT(key) DO UPDATE SET turned_on=@on
        """, ("key", key), ("on", turnedOn));
}

/// <summary>A catalog a test controls: its entries, the sources it finds and what its set-up does.</summary>
internal sealed class TestCatalog(string key, params CatalogEntry[] entries) : ISourceCatalog
{
    internal List<string> SetUps { get; } = [];

    internal Func<string, Result<CatalogSetUp>> SetUp { get; set; } = _ => Result<CatalogSetUp>.Ok(new CatalogSetUp([]));

    internal IReadOnlyList<CatalogSourceLink> Links { get; set; } = [];

    public string Key { get; } = key;

    public IReadOnlyList<CatalogEntry> Entries() => entries;

    public Task<Result<CatalogSetUp>> SetUpAsync(string entryKey, CancellationToken cancellationToken = default)
    {
        SetUps.Add(entryKey);
        return Task.FromResult(SetUp(entryKey));
    }

    public Task<IReadOnlyList<CatalogSourceLink>> FindSourcesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Links);

    internal static CatalogEntry Entry(string key, string name = "Entry") => new()
    {
        Key = key, Catalog = key.Split('.')[0], Name = name, Kind = CatalogKind.Events, Channels = 1,
        Hosts = [new CatalogHost(SourceFamily.Site, "mstar.uz")],
    };
}
