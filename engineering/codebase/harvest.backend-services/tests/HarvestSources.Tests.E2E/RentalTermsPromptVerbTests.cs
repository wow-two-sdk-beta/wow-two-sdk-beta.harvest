using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Marketplace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Module;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestSources.Tests.E2E;

public sealed class RentalTermsPromptVerbTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1";
    private const string ProviderSecret = "fixture-provider-key";
    private const string PrivateTemplate = "Synthetic private prompt";
    private const string AiEnabledSetting = "Harvest:Processing:Ai:Enabled";

    [Theory]
    [InlineData(null, null)]
    [InlineData("internal", "false")]
    [InlineData("public", "true")]
    public async Task Host_requires_internal_audience_and_explicit_opt_in_for_shared_ai(string? audience, string? enabled)
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            ["HarvestSources:Audience"] = audience,
            [AiEnabledSetting] = enabled,
        });
        await using var scope = host.Services.CreateAsyncScope();

        scope.ServiceProvider.GetService<IAiRunner>().Should().BeNull();
        var run = await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "fixture"]);
        run.Exit.Should().Be(1);
        run.Output.Should().BeEmpty();
        run.Error.Should().Contain(AiEnabledSetting);
    }

    [Fact]
    public async Task Host_uses_the_harvest_database_and_only_advertises_routes_with_configured_providers()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = UnreachableDatabase,
            ["Ai:Module:ConnectionString"] = "Host=wrong;Database=unrelated",
            ["Ai:Module:RetriesPerModel"] = "0",
            ["Ai:Providers:groq:Kind"] = "openai",
            ["Ai:Providers:groq:ApiKey"] = ProviderSecret,
            ["Ai:Providers:no-key:Kind"] = "openai",
            ["Ai:Providers:ollama:Kind"] = "ollama",
            [$"{RentalTermsOptions.SectionName}:Models:0"] = "Groq/openai/gpt-oss-20b",
            [$"{RentalTermsOptions.SectionName}:Models:1"] = "groq/openai/gpt-oss-20b",
            [$"{RentalTermsOptions.SectionName}:Models:2"] = "no-key/missing",
            [$"{RentalTermsOptions.SectionName}:Models:3"] = "unknown/missing",
            [$"{RentalTermsOptions.SectionName}:Models:4"] = "invalid",
            [$"{RentalTermsOptions.SectionName}:Models:5"] = "ollama/model",
        });
        await using var scope = host.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IAiRunner>().Should().NotBeNull();
        var ai = host.Services.GetRequiredService<IOptions<AiModuleOptions>>().Value;
        ai.ConnectionString.Should().Be(UnreachableDatabase);
        ai.RetriesPerModel.Should().Be(0);
        host.Services.GetRequiredService<IOptions<RentalTermsOptions>>().Value.Models
            .Should().Equal("groq/openai/gpt-oss-20b", "ollama/model");
    }

    [Fact]
    public async Task Local_host_migrates_ai_and_saves_private_file_and_stdin_as_immutable_versions()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
        });
        await host.ConfigureAsync();
        var checks = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        checks.Entries["database"].Status.Should().Be(HealthStatus.Healthy);
        checks.Entries["ai-database"].Status.Should().Be(HealthStatus.Healthy);

        string file = Path.GetTempFileName();
        (int Exit, string Output, string Error) first;
        try
        {
            await File.WriteAllBytesAsync(file, Draft(PrivateTemplate));
            first = await RunAsync(host.Services, ["prompt", "--file", file, "--by", "file-operator"]);
        }
        finally
        {
            File.Delete(file);
        }
        using var input = new MemoryStream(Draft("Second synthetic prompt"));
        var second = await RunAsync(host.Services, ["prompt", "--by", "stdin-operator", "--file", "-"], input);

        VersionOf(first).Should().Be(1);
        VersionOf(second).Should().Be(2);
        first.Output.Should().NotContain(PrivateTemplate).And.NotContain("file-operator");
        first.Error.Should().BeEmpty();
        second.Error.Should().BeEmpty();

        await using var scope = host.Services.CreateAsyncScope();
        var prompts = scope.ServiceProvider.GetRequiredService<IPromptsBroker>();
        var useCase = (await prompts.GetUseCaseAsync(RentalTermsDefinition.UseCase)).ValueOrThrow();
        useCase.PromptMode.Should().Be(PromptMode.Versioned);
        useCase.KeepOutputs.Should().BeFalse();
        useCase.Route.Should().BeEmpty();
        using var actualSchema = JsonDocument.Parse(useCase.OutputSchema!);
        using var expectedSchema = JsonDocument.Parse(RentalTermsDefinition.OutputSchema);
        JsonElement.DeepEquals(actualSchema.RootElement, expectedSchema.RootElement).Should().BeTrue();

        var promptsSaved = (await prompts.ListPromptsAsync(RentalTermsDefinition.UseCase)).ValueOrThrow();
        promptsSaved.Select(prompt => prompt.Version).Should().Equal(2, 1);
        promptsSaved.Select(prompt => prompt.CreatedBy).Should().Equal("stdin-operator", "file-operator");
        promptsSaved[1].SystemTemplate.Should().Be(PrivateTemplate);
        promptsSaved[1].Parameters.Should().Be(new PromptParameters
        {
            MaxOutputTokens = 512, Temperature = 0.2, Effort = "low",
        });
    }

    [Fact]
    public async Task Registration_refuses_a_rewrite_use_case_without_replacing_it()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
        });
        await host.ConfigureAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var prompts = scope.ServiceProvider.GetRequiredService<IPromptsBroker>();
        (await prompts.PutUseCaseAsync(new()
        {
            Key = RentalTermsDefinition.UseCase,
            PromptMode = PromptMode.Rewrite,
            OutputSchema = RentalTermsDefinition.OutputSchema,
            KeepOutputs = false,
        })).ValueOrThrow();
        using var input = new MemoryStream(Draft(PrivateTemplate));

        var run = await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "fixture"], input);

        run.Exit.Should().Be(1);
        run.Output.Should().BeEmpty();
        run.Error.Should().Contain("versioned").And.NotContain(PrivateTemplate);
        (await prompts.GetUseCaseAsync(RentalTermsDefinition.UseCase)).ValueOrThrow()
            .PromptMode.Should().Be(PromptMode.Rewrite);
        (await prompts.ListPromptsAsync(RentalTermsDefinition.UseCase)).ValueOrThrow().Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_arguments_and_drafts_never_echo_private_text()
    {
        await using var host = Build(new Dictionary<string, string?> { [AiEnabledSetting] = "true" });
        var usage = await RunAsync(host.Services, ["prompt", "--file", "-"]);
        usage.Exit.Should().Be(2);
        usage.Output.Should().BeEmpty();
        usage.Error.Should().Contain("Usage:");

        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(
            new { unexpectedPrivateField = PrivateTemplate }));
        var invalid = await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "fixture"], input);
        invalid.Exit.Should().Be(1);
        invalid.Output.Should().BeEmpty();
        invalid.Error.Should().Contain("JSON prompt draft").And.NotContain(PrivateTemplate)
            .And.NotContain("unexpectedPrivateField");

        using var unbound = new MemoryStream(Draft(PrivateTemplate, "{{title}}"));
        var missing = await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "fixture"], unbound);
        missing.Exit.Should().Be(1);
        missing.Output.Should().BeEmpty();
        missing.Error.Should().Contain("{{description}}").And.NotContain(PrivateTemplate);
    }

    private static byte[] Draft(string systemTemplate, string userTemplate = "{{ description }}") =>
        JsonSerializer.SerializeToUtf8Bytes(new PromptDraft
        {
            UseCase = RentalTermsDefinition.UseCase,
            SystemTemplate = systemTemplate,
            UserTemplate = userTemplate,
            By = "draft-author",
            Parameters = new() { MaxOutputTokens = 512, Temperature = 0.2, Effort = "low" },
        });

    private static int VersionOf((int Exit, string Output, string Error) run)
    {
        run.Exit.Should().Be(0, run.Error);
        using var doc = JsonDocument.Parse(run.Output);
        doc.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(new[] { "useCase", "version", "contentHash", "id" });
        doc.RootElement.GetProperty("useCase").GetString().Should().Be(RentalTermsDefinition.UseCase);
        doc.RootElement.GetProperty("contentHash").GetString().Should().HaveLength(12);
        return doc.RootElement.GetProperty("version").GetInt32();
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
            ["Harvest:Operator:ApiKey"] = "hv_" + new string('r', 32),
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Configure();
        return builder.Build();
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(IServiceProvider services, string[] args,
        Stream? input = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await RentalTermsPromptVerb.RunAsync(services, args, input ?? Stream.Null, output, error,
            CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
