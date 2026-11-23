using System.Text;
using System.Text.Json;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Module;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Tagging;

/// <summary>The module with the post-tags pass over one database, the AI module beside it in schema <c>ai</c>, and the
/// scripted caller in place of every provider: no live model call.</summary>
internal sealed class TagsHost(ServiceProvider services, AsyncServiceScope scope, string connectionString, FakeTimeProvider clock,
    ScriptedAiModelCaller caller) : IAsyncDisposable
{
    internal const string SystemTemplate = "Tag the post against this taxonomy:\n{{taxonomy}}";
    internal const string UserTemplate =
        "{{title}}\n{{sourceCategory}}\n{{dates}}\n{{place}}\n{{postedAt}}\n{{language}}\n{{text}}";

    internal string ConnectionString { get; } = connectionString;
    internal FakeTimeProvider Clock { get; } = clock;
    internal ScriptedAiModelCaller Caller { get; } = caller;
    internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
    internal IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    internal IPostTagsBroker Tags => scope.ServiceProvider.GetRequiredService<IPostTagsBroker>();
    internal IServiceProvider Scoped => scope.ServiceProvider;
    internal Guid ContainerId { get; private set; }
    internal Guid TenantId { get; private set; }
    internal Guid SourceId { get; private set; }
    internal Guid PipelineId { get; private set; }

    /// <summary>Starts a host over a fresh database.</summary>
    /// <param name="enabled">Whether <c>Harvest:Processing:Tags:Enabled</c> is set.</param>
    /// <param name="ai">Whether the host composes the AI module.</param>
    /// <param name="register">Whether to register the use case and one prompt version.</param>
    /// <param name="configure">Further registrations, such as the extraction pass, before the provider is built.</param>
    internal static async Task<TagsHost> StartAsync(bool enabled = true, bool ai = true, bool register = true,
        Action<IServiceCollection>? configure = null)
    {
        var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("tags");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));
        var caller = new ScriptedAiModelCaller(clock);
        var collection = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        collection.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        collection.AddHarvestPosts().AddHarvestDelivery();
        if (ai)
        {
            collection.AddAi().Configure(options =>
            {
                options.ConnectionString = connectionString;
                options.RetriesPerModel = 0;
                options.RetryDelay = TimeSpan.Zero;
            });
            collection.AddSingleton<IAiModelCaller>(caller);
        }
        collection.AddHarvestPostTags().Configure(options => options.Enabled = enabled);
        configure?.Invoke(collection);
        var provider = collection.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("test");
        if (ai)
            (await provider.GetRequiredService<IMigrationSetRegistry>().GetRunner(AiModuleConstants.MigrationSetName)
                .ApplyPendingAsync("test", CancellationToken.None)).ValueOrThrow();

        var host = new TagsHost(provider, provider.CreateAsyncScope(), connectionString, clock, caller);
        host.TenantId = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "events", Name = "Events" })).Id;
        host.ContainerId = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).Id;
        host.SourceId = await host.SourceAsync("mstar.uz");
        host.PipelineId = PostHost.Value(await host.Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = host.ContainerId, SourceId = host.SourceId,
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
        })).Id;
        if (ai && register)
            await host.RegisterPromptAsync();
        return host;
    }

    internal async Task<Guid> SourceAsync(string handle) =>
        PostHost.Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = ContainerId, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, Authority = 50, CheckPolicy = "web-direct",
        })).Id;

    internal async Task RegisterPromptAsync()
    {
        var prompts = Scoped.GetRequiredService<IPromptsBroker>();
        (await prompts.PutUseCaseAsync(new UseCaseModel
        {
            Key = PostTagsConstants.UseCase, PromptMode = PromptMode.Versioned, OutputSchema = PostTagsDefinition.OutputSchema,
            KeepOutputs = true,
        })).ValueOrThrow();
        (await prompts.SavePromptAsync(new PromptDraft
        {
            UseCase = PostTagsConstants.UseCase, SystemTemplate = SystemTemplate, UserTemplate = UserTemplate, By = "test",
        })).ValueOrThrow();
    }

    /// <summary>Saves a capture of <paramref name="key"/> with the given title and text, now.</summary>
    internal async Task<CaptureSaveResult> SaveAsync(string key, string title, string text, Guid? sourceId = null, string domain = "mstar.uz",
        PostEnvelope? envelope = null)
    {
        var capture = new Capture
        {
            ExternalKey = key, Url = $"https://{domain}/{key}", RequestUrl = $"https://{domain}/{key}", FinalUrl = $"https://{domain}/{key}",
            CapturedAtUtc = Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes($"{title}\n{text}"), ContentProjection = $"{title}\n{text}",
            HttpStatus = 200, ContentType = "text/html",
        };
        return PostHost.Value(await Posts.SaveCaptureAsync(ContainerId, sourceId ?? SourceId, null, capture,
            envelope ?? PostStoreTests.Envelope(title, text)));
    }

    /// <summary>Builds a schema-valid answer.</summary>
    internal static string Answer(string entityType = "event", object[]? domains = null, string[]? topics = null, object[]? flags = null,
        string timeframe = "upcoming") => JsonSerializer.Serialize(new
        {
            entityType,
            domains = domains ?? [new { node = "style-fashion", primary = true }],
            topics = topics ?? ["fashion-week"],
            about = "a fashion week",
            timeframe,
            items = 1,
            language = "en",
            confidence = 0.9,
            titleEn = "Tashkent Fashion Week",
            flags = flags ?? [],
        });

    internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await services.DisposeAsync();
        NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
    }
}
