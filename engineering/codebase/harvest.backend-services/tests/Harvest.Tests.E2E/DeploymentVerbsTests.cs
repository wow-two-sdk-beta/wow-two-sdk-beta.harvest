using Harvest.Api.Configurations;
using Harvest.Tests.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Meta;
using AwesomeAssertions;
using Npgsql;

namespace Harvest.Tests.E2E;

public sealed class DeploymentVerbsTests
{
    [Fact]
    public async Task Image_describes_every_migration_set_without_target_settings()
    {
        var run = await Run(["migrate", "sets", "--json"], new Dictionary<string, string?>());
        run.Exit.Should().Be(0, run.Error);
        run.Output.Should().Contain("\"harvest\"").And.Contain("migrate-result ").And.NotContain("\"distributed_cache\"");
    }

    [Fact]
    public async Task Configuration_check_rejects_invalid_values_and_unknown_worker_keys_without_echoing_secrets()
    {
        const string secret = "synthetic-secret-must-not-be-printed";
        var run = await Run(["config", "check"], new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = "",
            ["DatabaseSettings:ConnectionString"] = "",
            ["Harvest:Operator:ApiKey"] = secret,
            ["Harvest:Worker:Enabeld"] = "false",
        });
        run.Exit.Should().Be(1, run.Error);
        run.Output.Should().Contain("config-result ").And.Contain("Harvest:Worker:Enabeld").And.NotContain(secret);
    }

    [Fact]
    public async Task Configuration_check_requires_no_running_database_or_listener()
    {
        var run = await Run(["config", "check"], new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
            ["Harvest:Worker:Enabled"] = "false",
        });
        run.Exit.Should().Be(0, run.Error);
        run.Output.Should().Contain("config-result ");
    }

    [Fact]
    public async Task Deployment_migrate_applies_the_harvest_set_and_supports_the_rollback_check()
    {
        var settings = new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = await (await PostgresServer.SharedAsync()).CreateDatabaseAsync("verbs", "UTF8"),
            ["Harvest:Worker:Enabled"] = "false",
        };
        var applied = await Run(["migrate"], settings);
        applied.Exit.Should().Be(0, applied.Error);
        applied.Output.Should().Contain("migrate-result ").And.Contain("harvest");
        var checkedRun = await Run(["migrate", "check", "--json"], settings);
        checkedRun.Exit.Should().Be(0, checkedRun.Error);
        checkedRun.Output.Should().Contain("migrate-result ");
        var rerun = await Run(["migrate"], settings);
        rerun.Exit.Should().Be(0, rerun.Error);
    }

    [Fact]
    public async Task Deployed_boot_refuses_an_unmigrated_database_without_creating_tables()
    {
        string database = await (await PostgresServer.SharedAsync()).CreateDatabaseAsync("unmigrated", "UTF8");
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = database,
            ["DEPLOY_ENVIRONMENT"] = "dev",
            ["Harvest:Worker:Enabled"] = "false",
        });
        builder.Configure();
        await using var app = builder.Build();
        Func<Task> boot = () => app.ConfigureAsync();
        await boot.Should().ThrowAsync<InvalidOperationException>();
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog', 'information_schema')";
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0);
    }

    private static async Task<(int Exit, string Output, string Error)> Run(string[] args, Dictionary<string, string?> settings)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await ServiceHost.RunAsync(args,
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(settings);
                builder.Configure();
            },
            _ => throw new InvalidOperationException("A deployment verb must never configure or start the HTTP host."),
            null, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
