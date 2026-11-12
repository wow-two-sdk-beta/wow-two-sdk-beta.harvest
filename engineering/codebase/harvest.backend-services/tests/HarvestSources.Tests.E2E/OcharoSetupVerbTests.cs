using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Ocharo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

public sealed class OcharoSetupVerbTests
{
    [Fact]
    public async Task Setup_provisions_the_tenant_and_subscriptions_and_prints_their_ids_last()
    {
        await using var host = await StartAsync();

        var first = await Run(host.Services, ["setup", "--group", "afisha", "--run"]);
        var again = await Run(host.Services, ["setup", "--group", "afisha"]);

        first.Exit.Should().Be(0, first.Error);
        var result = JsonDocument.Parse(first.Output.TrimEnd().Split('\n')[^1]).RootElement;
        var subscriptions = result.GetProperty("subscriptions").EnumerateArray().ToArray();
        subscriptions.Should().NotBeEmpty().And.OnlyContain(item => item.GetProperty("groupId").GetString() == "afisha");
        result.GetProperty("runs").GetArrayLength().Should().Be(subscriptions.Length, "--run queues one capture per pipeline");
        again.Exit.Should().Be(0, again.Error);
        var repeated = JsonDocument.Parse(again.Output.TrimEnd().Split('\n')[^1]).RootElement;
        repeated.GetProperty("tenantId").GetGuid().Should().Be(result.GetProperty("tenantId").GetGuid());
        repeated.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid())
            .Should().BeEquivalentTo(subscriptions.Select(item => item.GetProperty("subscriptionId").GetGuid()));
        repeated.GetProperty("runs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Setup_with_refresh_queues_one_refresh_run_per_pipeline()
    {
        await using var host = await StartAsync();

        var refreshed = await Run(host.Services, ["setup", "--group", "afisha", "--refresh", "--refresh"]);

        refreshed.Exit.Should().Be(0, refreshed.Error);
        var result = JsonDocument.Parse(refreshed.Output.TrimEnd().Split('\n')[^1]).RootElement;
        var runs = result.GetProperty("runs").EnumerateArray().ToArray();
        runs.Should().HaveCount(result.GetProperty("subscriptions").GetArrayLength())
            .And.OnlyContain(item => item.GetProperty("kind").GetString() == "refresh");
    }

    [Fact]
    public async Task Status_reads_each_channels_cadence_in_one_short_line_and_writes_nothing()
    {
        await using var host = await StartAsync();

        var missing = await Run(host.Services, ["status", "--group", "afisha"]);
        await Run(host.Services, ["setup", "--group", "afisha", "--schedules"]);
        var status = await Run(host.Services, ["status", "--group", "afisha"]);
        var again = await Run(host.Services, ["status", "--group", "afisha"]);

        missing.Exit.Should().Be(1);
        status.Exit.Should().Be(0, status.Error);
        string last = status.Output.TrimEnd().Split('\n')[^1];
        last.Length.Should().BeLessThan(500);
        var result = JsonDocument.Parse(last).RootElement;
        result.GetProperty("tenant").GetString().Should().Be("ocharo");
        var lines = result.GetProperty("pipelines").EnumerateArray().Select(item => item.GetString()!).ToArray();
        lines.Should().NotBeEmpty().And.OnlyContain(line => line.StartsWith("afisha/") && line.Contains("capture 6h due ")
            && line.Contains("check 1d due "));
        again.Output.Should().Be(status.Output, "status reads and never queues or writes");
        (await Run(host.Services, ["status", "--force"])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Setup_gives_another_tenant_its_own_container_and_subscriptions()
    {
        await using var host = await StartAsync();

        var ocharo = await Run(host.Services, ["setup", "--group", "afisha"]);
        var probe = await Run(host.Services, ["setup", "--group", "afisha", "--tenant", "isolation-check"]);

        probe.Exit.Should().Be(0, probe.Error);
        var owner = JsonDocument.Parse(ocharo.Output.TrimEnd().Split('\n')[^1]).RootElement;
        var other = JsonDocument.Parse(probe.Output.TrimEnd().Split('\n')[^1]).RootElement;
        other.GetProperty("tenantId").GetGuid().Should().NotBe(owner.GetProperty("tenantId").GetGuid());
        other.GetProperty("containerId").GetGuid().Should().NotBe(owner.GetProperty("containerId").GetGuid());
        other.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid())
            .Should().NotIntersectWith(owner.GetProperty("subscriptions").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid()));
        (await Run(host.Services, ["setup", "--tenant", "Not A Slug"])).Exit.Should().Be(1);
    }

    [Theory]
    [InlineData("ocharo")]
    [InlineData("ocharo setup --group")]
    [InlineData("ocharo setup --force")]
    public async Task Setup_answers_its_usage_without_serving(string commandLine)
    {
        string[] args = commandLine.Split(' ');
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ServiceHost.RunAsync(args,
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
                });
                builder.Configure();
            },
            _ => throw new InvalidOperationException("A verb must not start the source host."),
            verbs => verbs.AddHostVerb(OcharoSetupVerb.Name, (services, verb, ct) =>
                OcharoSetupVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)),
            output, error, CancellationToken.None);

        exit.Should().Be(2, error.ToString());
        error.ToString().Should().Contain("Usage: ocharo setup");
        output.ToString().Should().BeEmpty();
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = "hv_OcharoSetupVerbTestOperator00000",
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        var host = builder.Build();
        await host.ConfigureAsync();
        return host;
    }

    private static async Task<(int Exit, string Output, string Error)> Run(IServiceProvider services, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await OcharoSetupVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
