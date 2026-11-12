using Npgsql;
using Testcontainers.PostgreSql;

namespace HarvestAdapters.Tests.Shared;

internal static class SourcePostgres
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Server = new(StartAsync);

    internal static async Task<string> NewDatabaseAsync()
    {
        PostgreSqlContainer server = await Server.Value;
        return new NpgsqlConnectionStringBuilder(server.GetConnectionString())
        {
            Database = "sources_" + Guid.NewGuid().ToString("N"),
        }.ConnectionString;
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        var server = new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build();
        await server.StartAsync();
        return server; // Testcontainers removes the server with this test process.
    }
}
