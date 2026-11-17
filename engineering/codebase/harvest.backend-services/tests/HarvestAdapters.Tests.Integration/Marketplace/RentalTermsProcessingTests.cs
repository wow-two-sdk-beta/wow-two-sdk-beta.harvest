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
    private const string Text = "A 500 USD deposit is required.";

    [Theory]
    [InlineData("rental-terms@1", "rental-terms@1:fixture-chat")]
    [InlineData(null, "saved-reading@1:fixture-chat")]
    public async Task Process_ShouldStoreOnlyVerbatimRentalEvidence(string? processor, string resultKey)
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
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("rental-terms-fixture");

        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Value(await registry.RegisterTenantAsync(new TenantRegistration { Slug = "rental-fixture", Name = "Rental fixture" }));
        var container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "rentals.example",
            Coverage = SourceCoverage.Whole, Completeness = SourceCompleteness.Complete,
            Posture = SourcePosture.OwnFetch, AiAllowed = true,
        }));
        var pipeline = Value(await registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = container.Id, SourceId = source.Id, Name = "Fixture rentals",
            Scope = new(),
            Schedules = [new()
            {
                Kind = WorkKind.Process, Mode = ScheduleMode.Manual,
                Stage = new() { BatchSize = 1, Processor = processor, Model = Model },
            }],
        }));

        var stages = scope.ServiceProvider.GetRequiredService<PipelineStages>();
        var process = (await stages.ListAsync(pipeline, source, CancellationToken.None)).Single(stage => stage.Kind == WorkKind.Process);
        process.ProcessorKeys.Should().BeEquivalentTo(ICaptureProcessor.DefaultKey, "rental-terms@1");
        process.Options.Processor.Should().Be("rental-terms@1");

        const string url = "https://rentals.example/listing/one";
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        var capture = Value(await posts.SaveCaptureAsync(container.Id, source.Id, null, new Capture
        {
            ExternalKey = "one", Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = clock.GetUtcNow(),
            Body = "listing fixture"u8.ToArray(), ContentProjection = "listing fixture",
        }, new PostEnvelope
        {
            Title = "Apartment", Text = Text, Language = new() { Code = "en" }, Kind = PostKind.Listing,
            Extraction = new() { NormVersion = "fixture", RulesVersion = "fixture" },
        }));

        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = Value(await runs.StartAsync(pipeline.Id, WorkKind.Process));
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        (await engine.ProcessNextAsync()).Should().BeTrue();
        Value(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Completed);
        chat.Input.Should().Be(Text);

        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT result::text FROM harvest_processing_results WHERE capture_id=@capture AND processor=@processor
            """, db);
        command.Parameters.AddWithValue("capture", capture.CaptureId);
        command.Parameters.AddWithValue("processor", resultKey);
        var stored = (await command.ExecuteScalarAsync()).Should().BeOfType<string>().Which;
        using var result = JsonDocument.Parse(stored);
        var terms = result.RootElement.GetProperty("terms").EnumerateArray().ToArray();
        terms.Should().ContainSingle();
        terms[0].GetProperty("kind").GetString().Should().Be("deposit");
        terms[0].GetProperty("value").GetString().Should().Be("500 USD");
        terms[0].GetProperty("evidence").GetString().Should().Be("500 USD deposit");
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success { Value: var value }
        ? value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

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
