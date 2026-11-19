using System.Text.RegularExpressions;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Ocharo;
using HarvestSources.Api.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

public sealed partial class SourcesVerbTests
{
    private const string AfishaGroup = "Afisha Uzbekistan";

    [Fact]
    public async Task List_prints_each_sources_id_family_handle_and_mode_and_writes_nothing()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup");
        var sources = await OcharoSources(host.Services);

        var list = await Run(host.Services, "processing", "--list");
        var again = await Run(host.Services, "processing", "--list");

        list.Exit.Should().Be(0, list.Error);
        var lines = Lines(list.Output);
        lines.Should().HaveCount(sources.Count).And.OnlyContain(line => ListLine().IsMatch(line));
        lines.Should().OnlyContain(line => line.EndsWith(" off"), "Ocharo setup registers sources closed to models");
        lines.Should().Contain(line => line.Contains(" site/www.afisha.uz "));
        lines.Select(line => Guid.Parse(line.Split(' ')[0])).Should().BeEquivalentTo(sources.Select(source => source.Id));
        again.Output.Should().Be(list.Output, "listing reads and never writes");
    }

    [Fact]
    public async Task Setting_a_group_by_name_prints_old_to_new_per_source_and_leaves_the_others()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup");
        var afisha = (await OcharoSources(host.Services)).Single(source => source.Handle == "www.afisha.uz");

        var first = await Run(host.Services, "processing", "--group", AfishaGroup.ToUpperInvariant(), "--mode", "auto");
        var again = await Run(host.Services, "processing", "--group", AfishaGroup, "--mode", "auto");

        first.Exit.Should().Be(0, first.Error);
        Lines(first.Output).Should().Equal($"{afisha.Id:D} site/www.afisha.uz off -> auto");
        again.Exit.Should().Be(0, again.Error);
        Lines(again.Output).Should().Equal($"{afisha.Id:D} site/www.afisha.uz auto -> auto");
        again.Error.Should().Contain("0 of 1 source(s) changed");
        var modes = (await OcharoSources(host.Services)).ToDictionary(source => source.Handle, source => source.ModelProcessing);
        modes["www.afisha.uz"].Should().Be(ModelProcessingMode.Auto);
        modes.Where(item => item.Key != "www.afisha.uz").Should().OnlyContain(item => item.Value == ModelProcessingMode.Off);
        (await Run(host.Services, "processing", "--list")).Output.Should().Contain($"{afisha.Id:D} site/www.afisha.uz auto");
    }

    [Fact]
    public async Task Setting_by_id_and_by_family_handle_sets_each_source_once()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup");
        var sources = await OcharoSources(host.Services);
        var afisha = sources.Single(source => source.Handle == "www.afisha.uz");
        var other = sources.First(source => source.Id != afisha.Id);

        var run = await Run(host.Services, "processing", "--source", "Site/WWW.Afisha.uz", "--source", other.Id.ToString("D"),
            "--group", AfishaGroup, "--mode", "manual");

        run.Exit.Should().Be(0, run.Error);
        Lines(run.Output).Should().HaveCount(2).And.OnlyContain(line => line.EndsWith(" off -> manual"));
        var modes = (await OcharoSources(host.Services)).ToDictionary(source => source.Id, source => source.ModelProcessing);
        modes[afisha.Id].Should().Be(ModelProcessingMode.Manual);
        modes[other.Id].Should().Be(ModelProcessingMode.Manual);
        modes.Where(item => item.Key != afisha.Id && item.Key != other.Id).Should().OnlyContain(item => item.Value == ModelProcessingMode.Off);
    }

    [Theory]
    [InlineData("--source", "nothing/here")]
    [InlineData("--group", "No such group")]
    public async Task A_selector_that_matches_nothing_refuses_the_whole_call_and_changes_nothing(string option, string value)
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup");

        var run = await Run(host.Services, "processing", "--source", "site/www.afisha.uz", option, value, "--mode", "auto");

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("sources processing:");
        run.Output.Should().BeEmpty();
        (await OcharoSources(host.Services)).Should().OnlyContain(source => source.ModelProcessing == ModelProcessingMode.Off);
    }

    [Fact]
    public async Task A_family_handle_in_two_tenants_is_ambiguous_until_the_tenant_is_named()
    {
        await using var host = await StartAsync();
        await Ocharo(host.Services, "setup", "--group", "afisha");
        await Ocharo(host.Services, "setup", "--group", "afisha", "--tenant", "isolation-check");

        var ambiguous = await Run(host.Services, "processing", "--source", "site/www.afisha.uz", "--mode", "auto");
        var narrowed = await Run(host.Services, "processing", "--source", "site/www.afisha.uz", "--mode", "auto", "--tenant", "isolation-check");
        var listed = await Run(host.Services, "processing", "--list");
        var missing = await Run(host.Services, "processing", "--list", "--tenant", "no-such-tenant");

        ambiguous.Exit.Should().Be(1);
        ambiguous.Error.Should().Contain("matches 2 sources");
        ambiguous.Output.Should().BeEmpty();
        narrowed.Exit.Should().Be(0, narrowed.Error);
        Lines(narrowed.Output).Should().ContainSingle().Which.Should().EndWith("site/www.afisha.uz off -> auto");
        Lines(listed.Output).Select(line => line.Split(' ')[^1]).Should().BeEquivalentTo(["off", "auto"]);
        (await OcharoSources(host.Services)).Should().ContainSingle().Which.ModelProcessing.Should().Be(ModelProcessingMode.Off);
        missing.Exit.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("list")]
    [InlineData("processing")]
    [InlineData("processing --mode auto")]
    [InlineData("processing --source site/www.afisha.uz")]
    [InlineData("processing --source site/www.afisha.uz --mode loud")]
    [InlineData("processing --source site/www.afisha.uz --mode")]
    [InlineData("processing --source site/www.afisha.uz --mode auto --mode off")]
    [InlineData("processing --list --mode auto")]
    [InlineData("processing --list --force")]
    public async Task Usage_is_answered_before_any_service_is_resolved(string commandLine)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        var run = await Run(services, commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        run.Exit.Should().Be(2);
        run.Error.Should().Contain("Usage: sources processing");
        run.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task The_verb_answers_its_usage_through_the_service_host_without_serving()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ServiceHost.RunAsync(["sources", "processing"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DB_CONNECTION"] = "Host=127.0.0.1;Port=1;Database=unreachable;Username=synthetic;Timeout=1",
                });
                builder.Configure();
            },
            _ => throw new InvalidOperationException("A verb must not start the source host."),
            verbs => verbs.AddHostVerb(SourcesVerb.Name, (services, verb, ct) =>
                SourcesVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)),
            output, error, CancellationToken.None);

        exit.Should().Be(2, error.ToString());
        error.ToString().Should().Contain("Usage: sources processing");
        output.ToString().Should().BeEmpty();
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static async Task<List<SourceModel>> OcharoSources(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Assert.IsType<Result<IReadOnlyList<TenantModel>>.Success>(
            await registry.ListTenantsAsync()).Value.Single(item => item.Slug == "ocharo");
        var sources = new List<SourceModel>();
        foreach (var container in Assert.IsType<Result<IReadOnlyList<ContainerModel>>.Success>(
            await registry.ListContainersAsync(tenant.Id)).Value)
            sources.AddRange(Assert.IsType<Result<IReadOnlyList<SourceModel>>.Success>(await registry.ListSourcesAsync(container.Id)).Value);
        return sources;
    }

    private static async Task Ocharo(IServiceProvider services, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await OcharoSetupVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        exit.Should().Be(0, error.ToString());
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
            ["Harvest:Operator:ApiKey"] = "hv_SourcesVerbTestOperator000000000",
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

    private static async Task<(int Exit, string Output, string Error)> Run(IServiceProvider services, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await SourcesVerb.RunAsync(services, arguments, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} [a-z_]+/\S+ (off|manual|auto)$")]
    private static partial Regex ListLine();
}
