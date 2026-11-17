using System.Net;
using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using static Harvest.Tests.Integration.Engine.EngineHost;

namespace Harvest.Tests.Integration.Engine;

public sealed class SourceAddressRoutingTests
{
    [Fact]
    public async Task Provider_request_uses_its_frozen_step_floor_without_an_operator_override()
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.Adapter.MinimumDelay = TimeSpan.FromSeconds(5);
        var pipeline = await fixture.RegisterAsync("teplofest");
        Data(await fixture.Runs.StartAsync(pipeline.Id));
        fixture.Adapter.MinimumDelay = TimeSpan.FromSeconds(1);

        (await fixture.ProcessAsync()).Should().BeTrue();

        (await fixture.ScalarAsync<DateTimeOffset>("SELECT next_allowed_at FROM harvest_host_state WHERE host_key='www.instagram.com'"))
            .Should().Be(fixture.Clock.GetUtcNow().AddSeconds(5));
    }

    [Fact]
    public async Task Publisher_sources_keep_their_identity_but_share_the_host_pace_and_health()
    {
        await using var fixture = await Fixture.StartAsync();
        var festival = await fixture.RegisterAsync("teplofest");
        var store = await fixture.RegisterAsync("teplostore_2022");
        Data(await fixture.Runs.SetReadingPaceAsync(festival.SourceId, 5000)).HostKey.Should().Be("www.instagram.com");
        var first = Data(await fixture.Runs.StartAsync(festival.Id));
        var second = Data(await fixture.Runs.StartAsync(store.Id));

        (await fixture.ProcessAsync()).Should().BeTrue();
        var following = fixture.ProcessAsync();
        for (int tick = 0; tick < 100 && !following.IsCompleted; tick++)
        {
            await Task.Delay(5);
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        (await following.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        (fixture.Adapter.RequestTimes[1] - fixture.Adapter.RequestTimes[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(5),
            "the provider requests must share the same persisted host delay");

        Data(await fixture.Runs.GetAsync(first.Id)).Status.Should().Be(RunStatus.Completed);
        Data(await fixture.Runs.GetAsync(second.Id)).Status.Should().Be(RunStatus.Completed);
        fixture.Adapter.Calls.Should().BeEquivalentTo(new[]
        {
            ("teplofest", "https://www.instagram.com/teplofest/", "www.instagram.com"),
            ("teplostore_2022", "https://www.instagram.com/teplostore_2022/", "www.instagram.com"),
        });
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_host_state")).Should().Be(1);
        var overview = Data(await fixture.Overview.ListOverviewAsync(fixture.ContainerId));
        overview.Should().HaveCount(2).And.OnlyContain(row => row.Health.HostKey == "www.instagram.com" && row.Health.MinDelayMs == 5000);
        overview.Select(row => row.Handle).Should().BeEquivalentTo(new[] { "teplofest", "teplostore_2022" });
    }

    [Fact]
    public async Task Frozen_origin_routes_continuations_and_rejects_other_hosts_after_adapter_changes()
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.Adapter.Continue = true;
        var pipeline = await fixture.RegisterAsync("teplofest", restrict: true);
        var queued = Data(await fixture.Runs.StartAsync(pipeline.Id));
        fixture.Adapter.Host = "changed.example.test";

        (await fixture.ProcessAsync()).Should().BeTrue();
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item")).Should().Be(2,
            "only the original target and same-origin continuation are admitted");
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        (await fixture.ProcessAsync()).Should().BeTrue();
        (await fixture.ProcessAsync()).Should().BeFalse();

        Data(await fixture.Runs.GetAsync(queued.Id)).Status.Should().Be(RunStatus.Completed);
        fixture.Adapter.Calls.Select(call => call.Target).Should().Equal(
            "https://www.instagram.com/teplofest/", "https://www.instagram.com/teplofest/page2");
        fixture.Adapter.AllowedDestinations.Should().OnlyContain(allowed => allowed);
    }

    [Theory]
    [InlineData("https://evil.example/teplofest/")]
    [InlineData("http://www.instagram.com/teplofest/")]
    [InlineData("https://www.instagram.com:8443/teplofest/")]
    public async Task Admission_rejects_an_untrusted_origin_even_if_the_pipeline_names_it(string target)
    {
        await using var fixture = await Fixture.StartAsync();
        var pipeline = await fixture.RegisterAsync("teplofest", url: target);

        (await fixture.Runs.StartAsync(pipeline.Id)).IsSuccess.Should().BeFalse();

        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item")).Should().Be(0);
        fixture.Adapter.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Passive_sections_use_the_declared_profile_origin_without_starting_work()
    {
        await using var fixture = await Fixture.StartAsync(passive: true);
        var festival = await fixture.RegisterAsync("teplofest");
        var store = await fixture.RegisterAsync("teplostore_2022");

        var pipelines = Data(await fixture.Registry.ListPipelinesAsync(fixture.ContainerId));
        pipelines.Should().HaveCount(2);
        pipelines.Select(row => row.Scope.Url).Should().BeEquivalentTo(new[]
            { "https://www.instagram.com/teplofest/", "https://www.instagram.com/teplostore_2022/" });
        pipelines.Should().OnlyContain(row => row.Schedules.Count == 0);
        festival.SourceId.Should().NotBe(store.SourceId);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_work_item")).Should().Be(0);
    }

    private sealed class Fixture(ServiceProvider provider, AsyncServiceScope scope, string database, FakeTimeProvider clock,
        PublisherAdapter adapter, Guid containerId) : IAsyncDisposable
    {
        internal PublisherAdapter Adapter { get; } = adapter;
        internal FakeTimeProvider Clock { get; } = clock;
        internal Guid ContainerId { get; } = containerId;
        internal IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        internal IRunBroker Runs => scope.ServiceProvider.GetRequiredService<IRunBroker>();
        internal ISourceOverviewBroker Overview => scope.ServiceProvider.GetRequiredService<ISourceOverviewBroker>();

        internal static async Task<Fixture> StartAsync(bool passive = false)
        {
            var database = (await PostgresServer.SharedAsync()).NewDatabase("source-address");
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
            var adapter = new PublisherAdapter { Passive = passive };
            var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock)
                .AddSingleton<IPageFetcher>(new FixtureFetcher(clock))
                .AddSingleton<IFetchClientFactory, NoNetworkClients>().AddSingleton<IChallengeDetector, NoChallenges>();
            services.AddHarvest().Configure(options => options.ConnectionString = database);
            services.AddHarvestFetching();
            services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery().AddHarvestSources();
            services.AddHarvestEngine();
            services.AddSingleton<ISourceAdapter>(adapter).AddSingleton<IStepBroker>(adapter);
            var provider = services.BuildServiceProvider(validateScopes: true);
            await provider.MigrateHarvestOnStartupAsync("source-address-fixture");
            var scope = provider.CreateAsyncScope();
            var container = Data(await scope.ServiceProvider.GetRequiredService<IRegistrationBroker>()
                .RegisterContainerAsync(new() { Kind = ContainerKind.Pool }));
            return new(provider, scope, database, clock, adapter, container.Id);
        }

        internal async Task<PipelineModel> RegisterAsync(string handle, bool restrict = false, string? url = null)
        {
            var source = Data(await Registry.RegisterSourceAsync(new()
            {
                ContainerId = ContainerId, Family = SourceFamily.Instagram, Handle = handle,
                Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
            }));
            if (Adapter.Passive)
                return Data(await Registry.ListPipelinesAsync(ContainerId)).Single(row => row.SourceId == source.Id);
            return Data(await Registry.DeclarePipelineAsync(new()
            {
                ContainerId = ContainerId, SourceId = source.Id, Name = handle,
                Scope = new() { Url = url ?? $"https://www.instagram.com/{handle}/", RestrictToPath = restrict },
                Limits = new() { MaxPages = 4 },
            }));
        }

        internal Task<bool> ProcessAsync() => scope.ServiceProvider.GetRequiredService<HarvestEngine>().ProcessNextAsync();

        internal async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return reader.GetFieldValue<T>(0);
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class PublisherAdapter : ISourceAdapter, IStepBroker, ISourceSectionCatalog
    {
        internal string Host { get; set; } = "www.instagram.com";
        internal bool Passive { get; init; }
        internal bool Continue { get; set; }
        internal TimeSpan? MinimumDelay { get; set; }
        internal List<(string Handle, string Target, string HostKey)> Calls { get; } = [];
        internal List<bool> AllowedDestinations { get; } = [];
        internal List<DateTimeOffset> RequestTimes { get; } = [];
        public string Key => "publisher-fixture";
        public string AdapterKey => Key;
        public string StepKey => "profile";
        public SourceFamily Family => SourceFamily.Instagram;
        public bool Serves(SourceModel source) => source.Family == Family;
        public AdapterDescriptor Describe(SourceModel source) => new()
        {
            SourceUrl = $"https://{Host}/{source.Handle}/", HostKey = Host,
            Steps = [new() { Key = StepKey, Role = StepRole.Enumerate, Requires = new() { MinDelay = MinimumDelay } }],
            Plans = new Dictionary<WorkKind, IReadOnlyList<string>> { [WorkKind.Capture] = [StepKey] },
        };
        public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) => Passive
            ? [new("Profile", Describe(source).SourceUrl!, "en") { RestrictToPath = false }] : [];
        public async Task<StepResult> ExecuteAsync(StepItem item, IStepContext context, CancellationToken cancellationToken = default)
        {
            await using var lease = await context.AcquireRequestAsync(cancellationToken);
            if (lease.Outcome != FetchOutcome.Ok)
                return new() { Outcome = lease.Outcome, RetryAfter = lease.RetryAfter };
            Calls.Add((context.Source.Handle, item.Target, context.Plan.HostKey));
            RequestTimes.Add(context.Clock.GetUtcNow());
            AllowedDestinations.Add(context.AllowsDestination(new Uri(item.Target)));
            await lease.CompleteAsync(FetchOutcome.Ok, cancellationToken);
            return new StepResult
            {
                Outcome = FetchOutcome.Ok, Usage = new() { Requests = 1 },
                Next = Continue && item.Target.EndsWith('/')
                    ? [new() { StepKey = StepKey, Target = item.Target + "page2" },
                       new() { StepKey = StepKey, Target = "https://evil.example/teplofest/page2" },
                       new() { StepKey = StepKey, Target = "http://www.instagram.com/teplofest/page2" }]
                    : [],
            };
        }
    }

    private sealed class NoNetworkClients : IFetchClientFactory
    {
        public HttpClient CreateClient(CookieContainer? cookies = null) => new(new NoNetworkHandler());
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The source-address fixture must not send network requests.");
    }
    private sealed class NoChallenges : IChallengeDetector
    {
        public string Key => "fixture";
        public ChallengeVerdict? Detect(ChallengeProbe probe) => null;
    }
}
