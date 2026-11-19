using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Processing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Tests.E2E;

/// <summary>The AI operator controls on the source host: <c>api/processing</c> re-runs, their status and eval sets for the
/// operator only, each mirrored by a <c>processing</c> verb, and the AI module's own API mounted under <c>api/ai</c> for
/// operators while shared AI is on. A scripted caller stands in for every provider.</summary>
public sealed class ProcessingRoutesTests
{
    private const string OperatorKey = "hv_ProcessingRoutesOperator00000000";
    private const string AiEnabledSetting = "Harvest:Processing:Ai:Enabled";

    [Fact]
    public async Task Processing_routes_serve_the_operator_only_and_mirror_the_verbs()
    {
        await using var host = await StartAsync(ai: true);
        using var client = Client(host, OperatorKey);
        var postId = await SeedAsync(host.Services);
        using var reader = Client(host, await TenantKeyAsync(client));

        (await reader.PostAsJsonAsync("/api/processing/reruns", new { allSources = true, dryRun = true })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await reader.GetAsync($"/api/processing/reruns/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reader.PostAsJsonAsync("/api/processing/eval-sets", new { set = "x", useCase = "post-tags" })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/api/ai/usecases")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a tenant holds no ai.* scope");
        using (var anonymous = host.GetTestClient())
            (await anonymous.GetAsync("/api/ai/usecases")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var dry = await client.PostAsJsonAsync("/api/processing/reruns", new { allSources = true, useCases = new[] { "post-tags" }, dryRun = true });
        dry.StatusCode.Should().Be(HttpStatusCode.OK);
        var counts = await DataAsync(dry);
        counts.GetProperty("queued").GetInt64().Should().Be(1);
        (!counts.TryGetProperty("runId", out var noRun) || noRun.ValueKind == JsonValueKind.Null).Should().BeTrue("a dry run queues no run");

        var queued = await client.PostAsJsonAsync("/api/processing/reruns", new { postIds = new[] { postId }, useCases = new[] { "post-tags" } });
        queued.StatusCode.Should().Be(HttpStatusCode.Accepted);
        string runId = (await DataAsync(queued)).GetProperty("runId").GetString()!;
        queued.Headers.Location!.ToString().Should().Be($"/api/processing/reruns/{runId}");
        var status = await DataAsync(await client.GetAsync($"/api/processing/reruns/{runId}"));
        status.GetProperty("remaining").GetProperty("post-tags").GetProperty("waiting").GetInt64().Should().Be(1);
        status.GetProperty("done").GetBoolean().Should().BeFalse();
        (await client.GetAsync($"/api/processing/reruns/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/processing/reruns", new { allSources = true, postIds = new[] { postId } })).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        var verbStatus = await VerbAsync(host.Services, "status", "--run", runId);
        verbStatus.Exit.Should().Be(0, verbStatus.Error);
        using (var line = JsonDocument.Parse(verbStatus.Output))
            line.RootElement.GetProperty("runId").GetString().Should().Be(runId);
        var verbDry = await VerbAsync(host.Services, "rerun", "--all-sources", "--use-case", "post-tags", "--dry-run");
        verbDry.Exit.Should().Be(0, verbDry.Error);
        verbDry.Output.TrimEnd().Split('\n').Should().ContainSingle();
        using (var line = JsonDocument.Parse(verbDry.Output))
            line.RootElement.GetProperty("dryRun").GetBoolean().Should().BeTrue();
        (await VerbAsync(host.Services, "rerun")).Exit.Should().Be(1, "a re-run names exactly one scope");
        (await VerbAsync(host.Services, "rerun", "--posts", "not-a-guid")).Exit.Should().Be(2);
        (await VerbAsync(host.Services, "status", "--run", Guid.NewGuid().ToString())).Exit.Should().Be(1);

        var set = await client.PostAsJsonAsync("/api/processing/eval-sets", new { set = "e2e-tags", useCase = "post-tags", sample = 5, seed = 3 });
        set.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(set)).GetProperty("added").GetInt32().Should().Be(1);
        var items = await DataAsync(await client.GetAsync("/api/ai/evals/sets/e2e-tags/items"));
        items.GetArrayLength().Should().Be(1, "the AI API serves the set to the operator");
        items[0].GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).Should().Contain("unreviewed");
        var verbSet = await VerbAsync(host.Services, "eval-set", "--set", "e2e-tags", "--use-case", "post-tags", "--sample", "5", "--seed", "3");
        verbSet.Exit.Should().Be(0, verbSet.Error);
        using (var line = JsonDocument.Parse(verbSet.Output))
            line.RootElement.GetProperty("skipped").GetInt32().Should().Be(1, "the post is in the set already");
        verbSet.Output.Should().NotContain("Moda");
    }

    [Fact]
    public async Task Ai_routes_stay_unmounted_and_reruns_refused_without_shared_ai()
    {
        await using var host = await StartAsync(ai: false);
        using var client = Client(host, OperatorKey);

        (await client.GetAsync("/api/ai/usecases")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsJsonAsync("/api/processing/reruns", new { allSources = true, dryRun = true })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/api/processing/eval-sets", new { set = "x", useCase = "post-tags" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
    }

    private static async Task<WebApplication> StartAsync(bool ai)
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
            [AiEnabledSetting] = ai ? "true" : "false",
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        builder.Services.AddSingleton<IAiModelCaller>(new ScriptedAiModelCaller());
        var host = builder.Build();
        await host.ConfigureAsync();
        await host.StartAsync();
        if (ai)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var prompts = scope.ServiceProvider.GetRequiredService<IPromptsBroker>();
            (await prompts.PutUseCaseAsync(new UseCaseModel
            {
                Key = PostTagsConstants.UseCase, PromptMode = PromptMode.Versioned, OutputSchema = PostTagsDefinition.OutputSchema,
                KeepOutputs = true,
            })).ValueOrThrow();
            (await prompts.SavePromptAsync(new PromptDraft
            {
                UseCase = PostTagsConstants.UseCase, SystemTemplate = "Tag.\n{{taxonomy}}", UserTemplate = "{{title}}\n{{text}}", By = "test",
            })).ValueOrThrow();
        }
        return host;
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

    private static async Task<Guid> SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var source = (await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "fashion.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, ModelProcessing = ModelProcessingMode.Manual,
        })).ValueOrThrow();
        const string text = "Moda haftaligi dekabrda bo'ladi.";
        var capture = new Capture
        {
            ExternalKey = "fw", Url = "https://fashion.example/fw", RequestUrl = "https://fashion.example/fw", FinalUrl = "https://fashion.example/fw",
            CapturedAtUtc = DateTimeOffset.UtcNow, Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, HttpStatus = 200,
            ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Event, Title = "Toshkent moda haftaligi", Text = text, Language = new PostLanguage { Code = "uz" },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        var saved = (await scope.ServiceProvider.GetRequiredService<IPostBroker>()
            .SaveCaptureAsync(container.Id, source.Id, null, capture, envelope)).ValueOrThrow();
        (await registry.UpdateSourceAsync(source.Id, new SourceUpdate { ModelProcessing = ModelProcessingMode.Auto })).ValueOrThrow();
        return saved.Post.Id;
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
}
