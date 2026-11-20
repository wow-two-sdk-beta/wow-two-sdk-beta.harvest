using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Processing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;
using WoW.Two.Sdk.Beta.Be.Meta;

namespace HarvestSources.Tests.E2E;

/// <summary>The AI spend guard on the source host: its <c>Harvest:Processing:Budget</c> settings bind and validate, and
/// <c>GET api/processing/budget</c> serves the operator only, mirrored by the <c>processing budget</c> verb. No model call.</summary>
public sealed class ProcessingBudgetTests
{
    private const string OperatorKey = "hv_ProcessingBudgetOperator00000000";
    private const string AiEnabledSetting = "Harvest:Processing:Ai:Enabled";
    private const string Limit = "Harvest:Processing:Budget:LimitUsd";
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1";

    [Fact]
    public async Task Budget_route_serves_the_operator_only_and_the_verb_prints_the_same_reading()
    {
        await using var host = await StartAsync(new Dictionary<string, string?> { [AiEnabledSetting] = "true", [Limit] = "10", [$"{ProcessingBudgetConstants.SectionName}:SoftShare"] = "0.5" });
        using var client = Client(host, OperatorKey);

        var response = await client.GetAsync("/api/processing/budget");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reading = await DataAsync(response);
        reading.GetProperty("state").GetString().Should().Be("open");
        reading.GetProperty("limitMicroUsd").GetInt64().Should().Be(10_000_000);
        reading.GetProperty("softMicroUsd").GetInt64().Should().Be(5_000_000);
        reading.GetProperty("spentMicroUsd").GetInt64().Should().Be(0);
        reading.GetProperty("limitUsd").GetDecimal().Should().Be(10m);
        reading.GetProperty("windowStartUtc").GetDateTimeOffset().Day.Should().Be(1);
        reading.GetProperty("rows").GetArrayLength().Should().Be(0);

        using var reader = Client(host, await TenantKeyAsync(client));
        (await reader.GetAsync("/api/processing/budget")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var anonymous = host.GetTestClient())
            (await anonymous.GetAsync("/api/processing/budget")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var verb = await VerbAsync(host.Services, "budget");
        verb.Exit.Should().Be(0, verb.Error);
        string last = verb.Output.TrimEnd().Split('\n')[^1];
        verb.Output.TrimEnd().Split('\n').Should().ContainSingle("the verb prints one JSON line");
        using var line = JsonDocument.Parse(last);
        line.RootElement.GetProperty("state").GetString().Should().Be("open");
        line.RootElement.GetProperty("limitMicroUsd").GetInt64().Should().Be(10_000_000);
        line.RootElement.GetProperty("spentUsd").GetDecimal().Should().Be(0m);
        last.Should().Contain("\"limitUsd\":10.0000").And.Contain("\"softUsd\":5.0000").And.Contain("\"spentUsd\":0.0000");
        (await VerbAsync(host.Services, "budget", "--extra", "x")).Exit.Should().Be(2, "the verb takes no option");
    }

    [Fact]
    public async Task Budget_is_off_without_a_limit_or_without_shared_ai_and_the_verb_still_exits_zero()
    {
        await using var noLimit = await StartAsync(new Dictionary<string, string?> { [AiEnabledSetting] = "true" });
        using var noLimitClient = Client(noLimit, OperatorKey);
        (await DataAsync(await noLimitClient.GetAsync("/api/processing/budget"))).GetProperty("state").GetString().Should().Be("off");
        var noLimitVerb = await VerbAsync(noLimit.Services, "budget");
        noLimitVerb.Exit.Should().Be(0, noLimitVerb.Error);
        using (var line = JsonDocument.Parse(noLimitVerb.Output.TrimEnd().Split('\n')[^1]))
            line.RootElement.GetProperty("state").GetString().Should().Be("off");

        await using var noAi = await StartAsync(new Dictionary<string, string?> { [AiEnabledSetting] = "false", [Limit] = "10" });
        using var noAiClient = Client(noAi, OperatorKey);
        (await DataAsync(await noAiClient.GetAsync("/api/processing/budget"))).GetProperty("state").GetString().Should().Be("off");
        var noAiVerb = await VerbAsync(noAi.Services, "budget");
        noAiVerb.Exit.Should().Be(0, noAiVerb.Error);
        using (var line = JsonDocument.Parse(noAiVerb.Output.TrimEnd().Split('\n')[^1]))
            line.RootElement.GetProperty("state").GetString().Should().Be("off");
    }

    [Fact]
    public async Task Host_binds_the_budget_options_and_refuses_values_outside_their_range()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [Limit] = "200",
            [$"{ProcessingBudgetConstants.SectionName}:SoftShare"] = "0.75",
            [$"{ProcessingBudgetConstants.SectionName}:CycleStartDay"] = "15",
            [$"{ProcessingBudgetConstants.SectionName}:SinceUtc"] = "2026-11-01T00:00:00Z",
            [$"{ProcessingBudgetConstants.SectionName}:RefreshInterval"] = "00:00:30",
        });

        var options = host.Services.GetRequiredService<IOptions<ProcessingBudgetOptions>>().Value;

        options.LimitUsd.Should().Be(200m);
        options.SoftShare.Should().Be(0.75m);
        options.CycleStartDay.Should().Be(15);
        options.SinceUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        options.RefreshInterval.Should().Be(TimeSpan.FromSeconds(30));
        await using (var defaults = Build(new Dictionary<string, string?>()))
        {
            var unset = defaults.Services.GetRequiredService<IOptions<ProcessingBudgetOptions>>().Value;
            (unset.LimitUsd, unset.SoftShare, unset.CycleStartDay, unset.SinceUtc).Should().Be(((decimal?)null, 0.8m, 1, (DateTimeOffset?)null),
                "the guard is off by default");
        }

        await using var invalid = Build(new Dictionary<string, string?> { [$"{ProcessingBudgetConstants.SectionName}:CycleStartDay"] = "31" });
        FluentActions.Invoking(() => invalid.Services.GetRequiredService<IOptions<ProcessingBudgetOptions>>().Value)
            .Should().Throw<OptionsValidationException>().WithMessage("*CycleStartDay*");
    }

    [Theory]
    [InlineData("Harvest:Processing:Budget:LimitUsd", "10", 0)]
    [InlineData("Harvest:Processing:Budget:LimitUsd", "-1", 1)]
    [InlineData("Harvest:Processing:Budget:SoftShare", "1.5", 1)]
    [InlineData("Harvest:Processing:Budget:CycleStartDay", "29", 1)]
    [InlineData("Harvest:Processing:Budget:RefreshInterval", "00:00:00", 1)]
    public async Task Configuration_check_rejects_a_bad_budget_setting(string key, string value, int expected)
    {
        var run = await Run(["config", "check"], new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = UnreachableDatabase,
            ["Harvest:Operator:ApiKey"] = "hv_12345678901234567890123456789012",
            [key] = value,
        });

        run.Exit.Should().Be(expected, run.Output + run.Error);
        (run.Output + run.Error).Should().Contain("config-result ");
    }

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = OperatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Ai:Module:RetriesPerModel"] = "0",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Configure();
        builder.Services.AddSingleton<IAiModelCaller>(new ScriptedAiModelCaller());
        var host = builder.Build();
        await host.ConfigureAsync();
        await host.StartAsync();
        return host;
    }

    private static WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = UnreachableDatabase,
            ["Harvest:Operator:ApiKey"] = OperatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Configure();
        return builder.Build();
    }

    private static HttpClient Client(WebApplication host, string key)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<string> TenantKeyAsync(HttpClient client)
    {
        string tenant = (await DataAsync(await client.PostAsJsonAsync("/api/tenants", new { name = "Reader", slug = "reader" })))
            .GetProperty("id").GetString()!;
        return (await DataAsync(await client.PostAsJsonAsync($"/api/tenants/{tenant}/api-keys",
            new { name = "Reader", scopes = new[] { "posts:read", "pipelines:run" } }))).GetProperty("secret").GetString()!;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<(int Exit, string Output, string Error)> VerbAsync(IServiceProvider services, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ProcessingVerb.RunAsync(services, args, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    private static async Task<(int Exit, string Output, string Error)> Run(string[] args, Dictionary<string, string?> settings)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await ServiceHost.RunAsync(args,
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(settings);
                builder.Configure();
            },
            _ => throw new InvalidOperationException("Deployment commands must not start the source host."),
            null, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
