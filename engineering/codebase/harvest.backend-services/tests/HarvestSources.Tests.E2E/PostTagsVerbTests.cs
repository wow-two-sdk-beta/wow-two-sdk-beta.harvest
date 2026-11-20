using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Tagging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Tests.E2E;

/// <summary>The <c>post-tags</c> host verb on the source host: prompt registration, a run, a backfill and a report,
/// answered by the scripted caller — no live model call, and no post text in the report.</summary>
public sealed class PostTagsVerbTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1";
    private const string AiEnabledSetting = "Harvest:Processing:Ai:Enabled";
    private const string PrivateTemplate = "Synthetic private post-tags prompt";
    private const string PostText = "Moda haftaligi dekabrda bo'ladi.";

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Host_refuses_post_tags_without_shared_ai(string? enabled)
    {
        await using var host = Build(new Dictionary<string, string?> { [AiEnabledSetting] = enabled });

        var run = await RunAsync(host.Services, ["report"]);

        run.Exit.Should().Be(1);
        run.Output.Should().BeEmpty();
        run.Error.Should().Contain(AiEnabledSetting);
        host.Services.GetService<IOptions<PostTagsOptions>>()!.Value.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Host_keeps_only_tag_routes_whose_provider_is_configured()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["Ai:Providers:groq:Kind"] = "openai",
            ["Ai:Providers:groq:ApiKey"] = "fixture-provider-key",
            [$"{PostTagsConstants.SectionName}:Models:0"] = "anthropic/claude-haiku-5-5",
            [$"{PostTagsConstants.SectionName}:Models:1"] = "Groq/openai/gpt-oss-20b",
            [$"{PostTagsConstants.SectionName}:MaxTextChars"] = "3000",
        });

        var options = host.Services.GetRequiredService<IOptions<PostTagsOptions>>().Value;

        options.Models.Should().Equal("groq/openai/gpt-oss-20b");
        options.MaxTextChars.Should().Be(3000);
    }

    [Fact]
    public async Task Prompt_saves_versions_under_the_code_schema_and_refuses_unbound_drafts()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
        });
        await host.ConfigureAsync();

        var first = await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "operator"], Draft());
        var second = await RunAsync(host.Services, ["prompt", "--file", "-"], Draft());

        Version(first).Should().Be(1);
        Version(second).Should().Be(2);
        first.Output.Should().NotContain(PrivateTemplate);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var prompts = scope.ServiceProvider.GetRequiredService<IPromptsBroker>();
            var useCase = (await prompts.GetUseCaseAsync(PostTagsConstants.UseCase)).ValueOrThrow();
            useCase.PromptMode.Should().Be(PromptMode.Versioned);
            useCase.KeepOutputs.Should().BeTrue();
            using var actual = JsonDocument.Parse(useCase.OutputSchema!);
            using var expected = JsonDocument.Parse(PostTagsDefinition.OutputSchema);
            JsonElement.DeepEquals(actual.RootElement, expected.RootElement).Should().BeTrue();
            (await prompts.ListPromptsAsync(PostTagsConstants.UseCase)).ValueOrThrow().Select(prompt => prompt.CreatedBy)
                .Should().Equal("draft-author", "operator");
        }

        var unknown = await RunAsync(host.Services, ["prompt", "--file", "-"], Draft(user: "{{description}} {{text}}"));
        unknown.Exit.Should().Be(1);
        unknown.Error.Should().Contain("postedAt").And.NotContain(PrivateTemplate);
        var untaxed = await RunAsync(host.Services, ["prompt", "--file", "-"], Draft(system: PrivateTemplate));
        untaxed.Exit.Should().Be(1);
        untaxed.Error.Should().Contain("{{taxonomy}}");
        (await RunAsync(host.Services, ["prompt", "--by", "x"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["unknown"])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Run_backfill_and_report_drive_the_pass_with_a_scripted_model()
    {
        var caller = new ScriptedAiModelCaller();
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = database,
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
        }, services => services.AddSingleton<IAiModelCaller>(caller));
        await host.ConfigureAsync();
        Version(await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "operator"], Draft())).Should().Be(1);
        var postId = await SeedPostAsync(host.Services);
        caller.Enqueue(ScriptedAiModelCaller.Answer(JsonSerializer.Serialize(new
        {
            entityType = "event", domains = new[] { new { node = "style-fashion", primary = true } }, topics = new[] { "fashion-week" },
            about = "a fashion week", timeframe = "upcoming", items = 1, language = "uz", confidence = 0.9,
            titleEn = "Tashkent Fashion Week", flags = new[] { new { field = "domains", reason = "no node", proposed = "fashion-weeks" } },
        })));

        var dry = await RunAsync(host.Services, ["run", "--post", postId.ToString(), "--dry-run"]);
        dry.Exit.Should().Be(0, dry.Error);
        using (var answer = JsonDocument.Parse(dry.Output))
        {
            answer.RootElement.GetProperty("status").GetString().Should().Be("done");
            answer.RootElement.GetProperty("stored").GetBoolean().Should().BeFalse();
            answer.RootElement.GetProperty("answer").GetProperty("domains")[0].GetProperty("node").GetString().Should().Be("style-fashion");
        }
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_tags")).Should().Be(0);

        var run = await RunAsync(host.Services, ["run", "--post", postId.ToString()]);
        run.Exit.Should().Be(0, run.Error);
        using (var stored = JsonDocument.Parse(run.Output))
            stored.RootElement.GetProperty("newRevision").GetInt32().Should().Be(2);
        caller.Requests.Should().ContainSingle("the run reused the dry run's stored answer");

        var backfill = await RunAsync(host.Services, ["backfill", "--since", "2020-01-01", "--limit", "10"]);
        backfill.Output.Trim().Should().Be("""{"queued":0}""");

        var report = await RunAsync(host.Services, ["report", "--since", "2020-01-01"]);
        report.Exit.Should().Be(0, report.Error);
        report.Output.Trim().Should().NotContain("\n").And.NotContain("Moda").And.NotContain("Fashion Week");
        using (var counts = JsonDocument.Parse(report.Output))
        {
            counts.RootElement.GetProperty("statuses").GetProperty("done").GetInt64().Should().Be(1);
            counts.RootElement.GetProperty("entityTypes").GetProperty("event").GetInt64().Should().Be(1);
            counts.RootElement.GetProperty("domains").GetProperty("style-fashion").GetInt64().Should().Be(1);
            counts.RootElement.GetProperty("flags")[0].GetProperty("proposed").GetString().Should().Be("fashion-weeks");
            counts.RootElement.GetProperty("attempts").GetInt64().Should().Be(1);
        }

        (await RunAsync(host.Services, ["run", "--post", Guid.NewGuid().ToString()])).Exit.Should().Be(1);
        (await RunAsync(host.Services, ["run", "--post", "not-a-guid"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["backfill", "--limit", "0"])).Exit.Should().Be(2);
    }

    private static async Task<Guid> SeedPostAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var source = (await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "fashion.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).ValueOrThrow();
        var capture = new Capture
        {
            ExternalKey = "fw", Url = "https://fashion.example/fw", RequestUrl = "https://fashion.example/fw", FinalUrl = "https://fashion.example/fw",
            CapturedAtUtc = DateTimeOffset.UtcNow, Body = Encoding.UTF8.GetBytes(PostText), ContentProjection = PostText, HttpStatus = 200,
            ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Event, Title = "Toshkent moda haftaligi", Text = PostText, Language = new PostLanguage { Code = "uz" },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        var saved = (await scope.ServiceProvider.GetRequiredService<IPostBroker>()
            .SaveCaptureAsync(container.Id, source.Id, null, capture, envelope)).ValueOrThrow();
        return saved.Post.Id;
    }

    private static MemoryStream Draft(string? system = null, string? user = null) =>
        new(JsonSerializer.SerializeToUtf8Bytes(new PromptDraft
        {
            UseCase = PostTagsConstants.UseCase,
            SystemTemplate = system ?? PrivateTemplate + "\n{{taxonomy}}",
            UserTemplate = user ?? "{{title}}\n{{sourceCategory}}\n{{dates}}\n{{place}}\n{{postedAt}}\n{{language}}\n{{text}}",
            By = "draft-author",
        }));

    private static int Version((int Exit, string Output, string Error) run)
    {
        run.Exit.Should().Be(0, run.Error);
        using var doc = JsonDocument.Parse(run.Output);
        doc.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(new[] { "useCase", "version", "contentHash", "id" });
        return doc.RootElement.GetProperty("version").GetInt32();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static WebApplication Build(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null)
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
        services?.Invoke(builder.Services);
        return builder.Build();
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(IServiceProvider services, string[] args,
        Stream? input = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await PostTagsVerb.RunAsync(services, args, input ?? Stream.Null, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
