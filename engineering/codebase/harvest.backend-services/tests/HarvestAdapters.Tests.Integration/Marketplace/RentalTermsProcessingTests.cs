using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestAdapters.Tests.Integration.Marketplace;

[Collection(SourceDeliveryCollection.Name)]
public sealed class RentalTermsProcessingTests
{
    private const string Model = "fixture-chat";
    private const string Processor = "rental-terms@1";
    private const string ResultKey = "rental-terms@1:fixture-chat";
    private const string Text = "A 500 USD deposit is required.";

    [Fact]
    public async Task Process_ShouldStoreOnlyVerbatimRentalEvidence()
    {
        await using var host = await StartAsync();
        await using var scope = host.Provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Value(await registry.RegisterTenantAsync(new TenantRegistration { Slug = "rental-fixture", Name = "Rental fixture" }));
        var container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "rentals.example",
            Coverage = SourceCoverage.Whole, Completeness = SourceCompleteness.Complete,
            Posture = SourcePosture.OwnFetch, AiAllowed = true,
        }));
        var pipeline = Value(await registry.DeclarePipelineAsync(ProcessPipeline(container.Id, source.Id, null)));

        var stages = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        var process = (await stages.ListAsync(pipeline, source, CancellationToken.None)).Single(stage => stage.Kind == WorkKind.Process);
        process.ProcessorKeys.Should().BeEquivalentTo(ICaptureProcessor.DefaultKey, Processor);
        process.Options.Processor.Should().Be(Processor);

        var capture = await SaveAsync(scope.ServiceProvider, host.Clock, container.Id, source.Id, "https://rentals.example/listing/one");
        (await RunAsync(scope.ServiceProvider, pipeline.Id)).Should().Be(RunStatus.Completed);
        host.Chat.Input.Should().Be(Text);
        await ExpectTermsAsync(host.Connection, capture);
    }

    [Fact]
    public async Task Process_ShouldRunRentalTermsOnAHavenSource_OnlyAfterSetupAllowsAi()
    {
        await using var host = await StartAsync();
        await using var scope = host.Provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var provisioner = scope.ServiceProvider.GetRequiredService<MarketplaceProvisioner>();
        var setup = Value(await provisioner.EnsureAsync(MarketplaceProfiles.Haven));
        var apartments = setup.Searches.Single(search => search.Key == "apartments-rent");
        var imports = setup.Searches.Single(search => search.Key == "domtut-apartments-rent");
        Value(await registry.GetSourceAsync(apartments.SourceId)).AiAllowed.Should().BeFalse("collected sources start closed to models");
        const string listing = "https://www.olx.uz/d/obyavlenie/fixture-flat-ID00001.html";
        var pipeline = Value(await registry.DeclarePipelineAsync(ProcessPipeline(setup.ContainerId, apartments.SourceId, listing)));
        var capture = await SaveAsync(scope.ServiceProvider, host.Clock, setup.ContainerId, apartments.SourceId, listing);

        await RunAsync(scope.ServiceProvider, pipeline.Id);
        host.Chat.Input.Should().BeNull("a source closed to models never reaches the model");
        (await StoredAsync(host.Connection, capture)).Should().BeNull();

        Value(await provisioner.EnsureAsync(MarketplaceProfiles.Haven, allowAi: true));
        Value(await provisioner.EnsureAsync(MarketplaceProfiles.Haven));
        Value(await registry.GetSourceAsync(apartments.SourceId)).AiAllowed.Should().BeTrue("a later setup keeps the source open");
        Value(await registry.GetSourceAsync(imports.SourceId)).AiAllowed.Should().BeFalse("imports keep their consent posture");
        host.Clock.Advance(TimeSpan.FromMinutes(6));
        (await RunAsync(scope.ServiceProvider, pipeline.Id)).Should().Be(RunStatus.Completed);
        host.Chat.Input.Should().Be(Text);
        await ExpectTermsAsync(host.Connection, capture);
    }

    private static PipelineDeclaration ProcessPipeline(Guid containerId, Guid sourceId, string? url) => new()
    {
        ContainerId = containerId, SourceId = sourceId, Name = "Fixture rental terms", Scope = new() { Url = url },
        Schedules = [new()
        {
            Kind = WorkKind.Process, Mode = ScheduleMode.Manual,
            Stage = new() { BatchSize = 1, Processor = Processor, Model = Model },
        }],
    };

    private static async Task<Guid> SaveAsync(IServiceProvider services, TimeProvider clock, Guid containerId, Guid sourceId, string url)
    {
        var posts = services.GetRequiredService<IPostBroker>();
        var saved = Value(await posts.SaveCaptureAsync(containerId, sourceId, null, new Capture
        {
            ExternalKey = "one", Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = clock.GetUtcNow(),
            Body = "listing fixture"u8.ToArray(), ContentProjection = "listing fixture",
        }, new PostEnvelope
        {
            Title = "Apartment", Text = Text, Language = new() { Code = "en" }, Kind = PostKind.Listing,
            Extraction = new() { NormVersion = "fixture", RulesVersion = "fixture" },
        }));
        return saved.CaptureId ?? throw new Xunit.Sdk.XunitException("The fixture capture was not stored.");
    }

    private static async Task<RunStatus> RunAsync(IServiceProvider services, Guid pipelineId)
    {
        var runs = services.GetRequiredService<IRunBroker>();
        var run = Value(await runs.StartAsync(pipelineId, WorkKind.Process));
        var engine = services.GetRequiredService<HarvestEngine>();
        while (await engine.ProcessNextAsync())
        {
        }
        return Value(await runs.GetAsync(run.Id)).Status;
    }

    private static async Task<string?> StoredAsync(string connection, Guid capture)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT result::text FROM harvest_processing_results WHERE capture_id=@capture AND processor=@processor
            """, db);
        command.Parameters.AddWithValue("capture", capture);
        command.Parameters.AddWithValue("processor", ResultKey);
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task ExpectTermsAsync(string connection, Guid capture)
    {
        string stored = (await StoredAsync(connection, capture)).Should().BeOfType<string>().Which;
        using var result = JsonDocument.Parse(stored);
        var terms = result.RootElement.GetProperty("terms").EnumerateArray().ToArray();
        terms.Should().ContainSingle();
        terms[0].GetProperty("kind").GetString().Should().Be("deposit");
        terms[0].GetProperty("value").GetString().Should().Be("500 USD");
        terms[0].GetProperty("evidence").GetString().Should().Be("500 USD deposit");
    }

    private static async Task<Host> StartAsync()
    {
        string connection = await SourcePostgres.NewDatabaseAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var chat = new ScriptedChatClient();
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connection);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddHarvestListingSources();
        services.AddKeyedSingleton<IChatClient>(Model, chat);
        var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("rental-terms-fixture");
        return new(provider, connection, clock, chat);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success { Value: var value }
        ? value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed record Host(ServiceProvider Provider, string Connection, FakeTimeProvider Clock, ScriptedChatClient Chat)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class ScriptedChatClient : IChatClient
    {
        public string? Input { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Input = messages.Single(message => message.Role == ChatRole.User).Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, """
                {"terms":[
                    {"kind":"deposit","value":"500 USD","evidence":"500 USD deposit"},
                    {"kind":"utilities","value":"included","evidence":"Free electricity"},
                    {"kind":"invented","value":"yes","evidence":"500 USD deposit"},
                    {"kind":"advance_rent","value":"","evidence":"500 USD deposit"}
                ]}
                """)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() { }
    }
}
