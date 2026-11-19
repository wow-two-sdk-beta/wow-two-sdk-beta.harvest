using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Extraction;
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
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Tests.E2E;

/// <summary>The <c>extract</c> host verb on the source host: prompt registration per use case, a run, a backfill and a
/// report, answered by the scripted caller — no live model call, and no post text in the report.</summary>
public sealed class PostExtractionVerbTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1";
    private const string AiEnabledSetting = "Harvest:Processing:Ai:Enabled";
    private const string PrivateTemplate = "Synthetic private extraction prompt";
    private const string PostTitle = "Toshkent moda haftaligi";
    private const string PostText = "Moda haftaligi 12-14 dekabr kunlari bo'ladi. Kirish 50 000 so'm.";

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Host_refuses_extract_without_shared_ai(string? enabled)
    {
        await using var host = Build(new Dictionary<string, string?> { [AiEnabledSetting] = enabled });

        var run = await RunAsync(host.Services, ["report"]);

        run.Exit.Should().Be(1);
        run.Output.Should().BeEmpty();
        run.Error.Should().Contain(AiEnabledSetting);
    }

    [Fact]
    public async Task Host_binds_wanted_pairs_pins_and_only_configured_routes()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["Ai:Providers:groq:Kind"] = "openai",
            ["Ai:Providers:groq:ApiKey"] = "fixture-provider-key",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:EntityType"] = "event",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:Domain"] = "style-fashion",
            [$"{PostExtractionConstants.SectionName}:Wanted:1:EntityType"] = "listing",
            [$"{PostExtractionConstants.SectionName}:Wanted:1:Domain"] = "real-estate",
            [$"{PostExtractionConstants.SectionName}:Models:0"] = "anthropic/claude-sonnet-5-5",
            [$"{PostExtractionConstants.SectionName}:Models:1"] = "Groq/openai/gpt-oss-120b",
            [$"{PostExtractionConstants.SectionName}:PromptVersions:extract-event"] = "3",
        });

        var options = host.Services.GetRequiredService<IOptions<PostExtractionOptions>>().Value;

        options.Wanted.Should().Equal(new PostExtractionWant { EntityType = "event", Domain = "style-fashion" },
            new PostExtractionWant { EntityType = "listing", Domain = "real-estate" });
        options.Models.Should().Equal("groq/openai/gpt-oss-120b");
        options.PromptVersions.Should().Equal(new Dictionary<string, int> { ["extract-event"] = 3 });

        await using var invalid = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:EntityType"] = "news",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:Domain"] = "politics",
        });
        FluentActions.Invoking(() => invalid.Services.GetRequiredService<IOptions<PostExtractionOptions>>().Value)
            .Should().Throw<OptionsValidationException>().WithMessage("*Wanted*");
    }

    [Fact]
    public async Task Prompt_saves_versions_per_use_case_under_the_code_schema_and_refuses_unbound_drafts()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
        });
        await host.ConfigureAsync();

        Version(await RunAsync(host.Services, ["prompt", "--use-case", "event", "--file", "-", "--by", "operator"], Draft("extract-event")))
            .Should().Be(1);
        Version(await RunAsync(host.Services, ["prompt", "--use-case", "event", "--file", "-"], Draft("extract-event"))).Should().Be(2);
        var listing = await RunAsync(host.Services, ["prompt", "--use-case", "listing", "--file", "-"], Draft("extract-listing"));
        Version(listing).Should().Be(1);
        listing.Output.Should().NotContain(PrivateTemplate);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var prompts = scope.ServiceProvider.GetRequiredService<IPromptsBroker>();
            foreach (var key in PostExtractionConstants.UseCases)
            {
                var useCase = (await prompts.GetUseCaseAsync(key)).ValueOrThrow();
                useCase.PromptMode.Should().Be(PromptMode.Versioned);
                useCase.KeepOutputs.Should().BeTrue();
                using var actual = JsonDocument.Parse(useCase.OutputSchema!);
                using var expected = JsonDocument.Parse(PostExtractionDefinitions.OutputSchema(key)!);
                JsonElement.DeepEquals(actual.RootElement, expected.RootElement).Should().BeTrue();
            }
            (await prompts.ListPromptsAsync("extract-event")).ValueOrThrow().Select(prompt => prompt.CreatedBy)
                .Should().Equal("draft-author", "operator");
        }

        var crossed = await RunAsync(host.Services, ["prompt", "--use-case", "listing", "--file", "-"], Draft("extract-event"));
        crossed.Exit.Should().Be(1);
        crossed.Error.Should().Contain("extract-listing");
        var unknown = await RunAsync(host.Services, ["prompt", "--use-case", "event", "--file", "-"], Draft("extract-event", "{{text}} {{taxonomy}}"));
        unknown.Exit.Should().Be(1);
        unknown.Error.Should().Contain("titleEn").And.NotContain(PrivateTemplate);
        var untexted = await RunAsync(host.Services, ["prompt", "--use-case", "event", "--file", "-"], Draft("extract-event", "{{title}}"));
        untexted.Exit.Should().Be(1);
        untexted.Error.Should().Contain("{{text}}");
        (await RunAsync(host.Services, ["prompt", "--file", "-"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["prompt", "--use-case", "news", "--file", "-"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["unknown"])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Run_backfill_and_report_drive_extraction_with_a_scripted_model()
    {
        var caller = new ScriptedAiModelCaller();
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = database,
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:EntityType"] = "event",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:Domain"] = "style-fashion",
        }, services => services.AddSingleton<IAiModelCaller>(caller));
        await host.ConfigureAsync();
        using (var tagsDraft = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new PromptDraft
               {
                   UseCase = PostTagsConstants.UseCase, SystemTemplate = "Tag.\n{{taxonomy}}", UserTemplate = "{{title}}\n{{text}}", By = "operator",
               })))
            (await PostTagsVerb.RunAsync(host.Services, ["prompt", "--file", "-"], tagsDraft, TextWriter.Null, TextWriter.Null, CancellationToken.None))
                .Should().Be(0);
        Version(await RunAsync(host.Services, ["prompt", "--use-case", "event", "--file", "-"], Draft("extract-event"))).Should().Be(1);
        var postId = await SeedPostAsync(host.Services);

        var untagged = await RunAsync(host.Services, ["run", "--post", postId.ToString()]);
        untagged.Exit.Should().Be(1);
        untagged.Error.Should().Contain("post-tags");

        caller.Enqueue(ScriptedAiModelCaller.Answer(JsonSerializer.Serialize(new
        {
            entityType = "event", domains = new[] { new { node = "style-fashion", primary = true } }, topics = new[] { "fashion-week" },
            about = "a fashion week", timeframe = "upcoming", items = 1, language = "uz", confidence = 0.9,
            titleEn = "Tashkent Fashion Week", flags = Array.Empty<object>(),
        })));
        await using (var scope = host.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IPostTagsBroker>().ProcessDueAsync()).Should().Be(1);
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_work WHERE kind='extract-event'")).Should().Be(1);
        caller.Enqueue(ScriptedAiModelCaller.Answer(EventAnswer()));

        var dry = await RunAsync(host.Services, ["run", "--post", postId.ToString(), "--dry-run"]);
        dry.Exit.Should().Be(0, dry.Error);
        using (var answer = JsonDocument.Parse(dry.Output))
        {
            answer.RootElement.GetProperty("useCase").GetString().Should().Be("extract-event");
            answer.RootElement.GetProperty("stored").GetBoolean().Should().BeFalse();
            answer.RootElement.GetProperty("role").GetString().Should().Be("announcement");
            answer.RootElement.GetProperty("answer").GetProperty("occurrences").GetArrayLength().Should().Be(1);
        }
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_extractions")).Should().Be(0);

        var run = await RunAsync(host.Services, ["run", "--post", postId.ToString()]);
        run.Exit.Should().Be(0, run.Error);
        using (var stored = JsonDocument.Parse(run.Output))
            stored.RootElement.GetProperty("stored").GetBoolean().Should().BeTrue();
        caller.Requests.Should().HaveCount(2, "the run reused the dry run's stored answer");
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_work WHERE kind='extract-event'")).Should().Be(0);

        var backfill = await RunAsync(host.Services, ["backfill", "--since", "2020-01-01", "--limit", "10"]);
        backfill.Exit.Should().Be(0, backfill.Error);
        backfill.Output.Trim().Should().Be("""{"queued":0}""");

        var report = await RunAsync(host.Services, ["report", "--since", "2020-01-01"]);
        report.Exit.Should().Be(0, report.Error);
        report.Output.Trim().Should().NotContain("\n").And.NotContain("Moda").And.NotContain("12-14");
        using (var counts = JsonDocument.Parse(report.Output))
        {
            var events = counts.RootElement.GetProperty("useCases").GetProperty("extract-event");
            events.GetProperty("statuses").GetProperty("done").GetInt64().Should().Be(1);
            events.GetProperty("roles").GetProperty("announcement").GetInt64().Should().Be(1);
            events.GetProperty("flags")[0].GetProperty("field").GetString().Should().Be("occurrences");
            events.GetProperty("attempts").GetInt64().Should().Be(1);
            events.GetProperty("p95LatencyMs").GetInt64().Should().BePositive();
            counts.RootElement.GetProperty("useCases").GetProperty("extract-listing").GetProperty("queued").GetInt64().Should().Be(0);
        }

        (await RunAsync(host.Services, ["run", "--post", Guid.NewGuid().ToString()])).Exit.Should().Be(1);
        (await RunAsync(host.Services, ["run", "--post", "not-a-guid"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["backfill", "--limit", "0"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["backfill", "--source", Guid.NewGuid().ToString()])).Exit.Should().Be(2);
    }

    private static string EventAnswer() => JsonSerializer.Serialize(new
    {
        name = new { en = "Tashkent Fashion Week", original = "Toshkent moda haftaligi" },
        editionLabel = (string?)null,
        occurrences = new[]
        {
            new { starts = "2026-12-12", ends = (string?)"2026-12-14", precision = "day", certainty = "stated", evidence = "12-14 dekabr" },
            new { starts = "2027-01-20", ends = (string?)null, precision = "day", certainty = "stated", evidence = "20 yanvar" },
        },
        place = new { venue = (string?)null, city = "Tashkent", country = "Uzbekistan" },
        organizer = (string?)null,
        format = "runway",
        access = "ticketed",
        prices = new[] { new { amount = 50_000m, currency = "UZS", label = "entry", evidence = "Kirish 50 000 so'm" } },
        registration = new { opens = (object?)null, deadline = (object?)null, link = (string?)null },
        participation = new[] { "visitor" },
        status = "scheduled",
        relation = new { role = "announcement", changes = Array.Empty<string>(), status = (string?)null },
        fashion = (object?)null,
        flags = Array.Empty<object>(),
    });

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
            Kind = PostKind.Event, Title = PostTitle, Text = PostText, Language = new PostLanguage { Code = "uz" },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        return (await scope.ServiceProvider.GetRequiredService<IPostBroker>().SaveCaptureAsync(container.Id, source.Id, null, capture, envelope))
            .ValueOrThrow().Post.Id;
    }

    private static MemoryStream Draft(string useCase, string? user = null) =>
        new(JsonSerializer.SerializeToUtf8Bytes(new PromptDraft
        {
            UseCase = useCase,
            SystemTemplate = PrivateTemplate,
            UserTemplate = user ?? "{{title}}\n{{text}}\n{{dates}}\n{{prices}}\n{{entityType}}\n{{domains}}\n{{postedAt}}",
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
        int exit = await PostExtractionVerb.RunAsync(services, args, input ?? Stream.Null, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
