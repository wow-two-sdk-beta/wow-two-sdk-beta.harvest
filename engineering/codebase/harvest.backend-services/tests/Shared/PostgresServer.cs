using Npgsql;
using Testcontainers.PostgreSql;

namespace Harvest.Tests.Shared;

/// <summary>Runs one throwaway PostgreSQL server (the platform image, <c>postgres:18-alpine</c>) per test assembly and
/// hands out a fresh database per fixture, so suites run in parallel and never share rows by accident.</summary>
/// <remarks>Linked into each tier that needs a database; it needs Docker. The container outlives the fixtures and is
/// removed with the test process by the Testcontainers reaper.</remarks>
public sealed class PostgresServer
{
    /// <summary>Holds the image every tier tests against, the one the platform runs.</summary>
    public const string Image = "postgres:18-alpine";

    private static readonly Lazy<Task<PostgresServer>> SharedServer = new(StartAsync);

    private readonly PostgreSqlContainer container;

    private PostgresServer(PostgreSqlContainer container) => this.container = container;

    /// <summary>Gets the connection string of the server's own database.</summary>
    public string ConnectionString => container.GetConnectionString();

    /// <summary>Returns the assembly's server, starting it on first use.</summary>
    public static Task<PostgresServer> SharedAsync() => SharedServer.Value;

    /// <summary>Returns the connection string of a database that does not exist yet; Harvest's boot creates it.</summary>
    /// <param name="prefix">A lowercase prefix for the database name.</param>
    public string NewDatabase(string prefix) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = $"{prefix}_{Guid.NewGuid():N}" }.ConnectionString;

    /// <summary>Creates a database with an explicit encoding and the C locale, such as <c>SQL_ASCII</c>, and returns its
    /// connection string.</summary>
    /// <param name="prefix">A lowercase prefix for the database name.</param>
    /// <param name="encoding">The database encoding.</param>
    public async Task<string> CreateDatabaseAsync(string prefix, string encoding)
    {
        var builder = new NpgsqlConnectionStringBuilder(NewDatabase(prefix));
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{builder.Database}\" ENCODING '{encoding}' LC_COLLATE 'C' LC_CTYPE 'C' TEMPLATE template0";
        await command.ExecuteNonQueryAsync();
        return builder.ConnectionString;
    }

    private static async Task<PostgresServer> StartAsync()
    {
        var container = new PostgreSqlBuilder().WithImage(Image).Build();
        await container.StartAsync();
        return new PostgresServer(container);
    }
}
