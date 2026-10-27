using AwesomeAssertions;
using Harvest.Tests.Shared;

namespace Harvest.Tests.E2E;

/// <summary>The host refuses to boot on a database Harvest cannot use, and says why.</summary>
public sealed class StoreRefusalTests
{
    [Fact]
    public async Task Boot_ShouldFailNamingTheEncoding_WhenTheDatabaseIsNotUtf8()
    {
        var connectionString = await (await PostgresServer.SharedAsync()).CreateDatabaseAsync("e2e_ascii", "SQL_ASCII");
        await using var host = HarvestApiFixture.CreateHost(connectionString);

        var boot = () => host.CreateClient();

        boot.Should().Throw<Exception>()
            .Which.ToString().Should().Contain("Harvest refuses to start on this database: the database encoding is SQL_ASCII, not UTF8");
    }
}
