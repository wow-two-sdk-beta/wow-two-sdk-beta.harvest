using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Marketplace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

public sealed class MarketplaceSetupVerbTests
{
    [Fact]
    public async Task Setup_provisions_the_profile_and_prints_its_ids_last()
    {
        await using var host = await StartAsync();

        var first = await Run(host.Services, ["setup", "--profile", "haven", "--run"]);
        var again = await Run(host.Services, ["setup", "--profile", "haven"]);

        first.Exit.Should().Be(0, first.Error);
        var result = Last(first.Output);
        result.GetProperty("profile").GetString().Should().Be("haven");
        var searches = result.GetProperty("searches").EnumerateArray().ToArray();
        searches.Select(item => item.GetProperty("key").GetString()).Should().Equal("apartments-rent", "houses-rent", "daily-rent");
        searches.Select(item => item.GetProperty("subscriptionId").GetGuid()).Should().OnlyHaveUniqueItems();
        result.GetProperty("runs").GetArrayLength().Should().Be(searches.Length, "--run queues one capture per search");
        result.GetProperty("consumerKey").ValueKind.Should().Be(JsonValueKind.Null, "setup issues no key unless asked");
        again.Exit.Should().Be(0, again.Error);
        var repeated = Last(again.Output);
        foreach (string id in new[] { "tenantId", "containerId", "sourceId" })
            repeated.GetProperty(id).GetGuid().Should().Be(result.GetProperty(id).GetGuid());
        repeated.GetProperty("searches").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetGuid())
            .Should().Equal(searches.Select(item => item.GetProperty("subscriptionId").GetGuid()));
        repeated.GetProperty("runs").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Setup_issues_the_consumer_key_once_and_keeps_its_secret_off_stderr()
    {
        await using var host = await StartAsync();

        var issued = await Run(host.Services, ["setup", "--profile", "haven", "--issue-key"]);
        var reissued = await Run(host.Services, ["setup", "--profile", "haven", "--issue-key"]);
        var rotated = await Run(host.Services, ["setup", "--profile", "haven", "--rotate-key"]);
        var status = await Run(host.Services, ["status", "--profile", "haven"]);

        issued.Exit.Should().Be(0, issued.Error);
        var key = Last(issued.Output).GetProperty("consumerKey");
        string secret = key.GetProperty("secret").GetString()!;
        secret.Should().StartWith(key.GetProperty("prefix").GetString());
        issued.Error.Should().NotContain(secret);
        var kept = Last(reissued.Output).GetProperty("consumerKey");
        kept.GetProperty("id").GetGuid().Should().Be(key.GetProperty("id").GetGuid());
        kept.GetProperty("secret").ValueKind.Should().Be(JsonValueKind.Null, "a secret is readable only once");
        var replaced = Last(rotated.Output).GetProperty("consumerKey");
        replaced.GetProperty("id").GetGuid().Should().NotBe(key.GetProperty("id").GetGuid());
        replaced.GetProperty("secret").GetString().Should().NotBeNullOrEmpty().And.NotBe(secret);
        Last(status.Output).GetProperty("activeKeys").GetInt32().Should().Be(1, "rotation revokes the old key");
    }

    [Fact]
    public async Task Status_reads_each_search_cadence_in_one_short_line_and_writes_nothing()
    {
        await using var host = await StartAsync();

        var missing = await Run(host.Services, ["status", "--profile", "haven"]);
        await Run(host.Services, ["setup", "--profile", "haven", "--schedules"]);
        var status = await Run(host.Services, ["status", "--profile", "haven"]);
        var again = await Run(host.Services, ["status", "--profile", "haven"]);

        missing.Exit.Should().Be(1);
        status.Exit.Should().Be(0, status.Error);
        string last = status.Output.TrimEnd().Split('\n')[^1];
        last.Length.Should().BeLessThan(600);
        var lines = JsonDocument.Parse(last).RootElement.GetProperty("pipelines").EnumerateArray().Select(item => item.GetString()!).ToArray();
        lines.Should().HaveCount(3).And.OnlyContain(line => line.Contains("capture 2h due ") && line.Contains("check 1d due ")
            && line.EndsWith("; events 0", StringComparison.Ordinal));
        again.Output.Should().Be(status.Output, "status reads and never queues or writes");
    }

    [Theory]
    [InlineData("marketplace")]
    [InlineData("marketplace setup")]
    [InlineData("marketplace setup --profile")]
    [InlineData("marketplace setup --profile nowhere")]
    [InlineData("marketplace setup --profile haven --issue-key --rotate-key")]
    [InlineData("marketplace status --profile haven --run")]
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
            verbs => verbs.AddHostVerb(MarketplaceSetupVerb.Name, (services, verb, ct) =>
                MarketplaceSetupVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)),
            output, error, CancellationToken.None);

        exit.Should().Be(2, error.ToString());
        error.ToString().Should().Contain("Usage: marketplace setup").And.Contain("Profiles: haven");
        output.ToString().Should().BeEmpty();
    }

    private static JsonElement Last(string output) => JsonDocument.Parse(output.TrimEnd().Split('\n')[^1]).RootElement.Clone();

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
            ["Harvest:Operator:ApiKey"] = "hv_MarketSetupVerbTestOperator00000",
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
        int exit = await MarketplaceSetupVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
