using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Wires the real engine, PostgreSQL, parsers, posts and delivery while all fetches stay in memory.</summary>
internal sealed class EngineHost : IAsyncDisposable
{
    private EngineHost(ServiceProvider services, string connectionString, FakeTimeProvider clock, FixtureFetcher fetcher)
    {
        Services = services;
        ConnectionString = connectionString;
        Clock = clock;
        Fetcher = fetcher;
    }

    internal ServiceProvider Services { get; }
    internal string ConnectionString { get; }
    internal FakeTimeProvider Clock { get; }
    internal FixtureFetcher Fetcher { get; }

    internal static async Task<EngineHost> StartAsync()
    {
        var connection = (await PostgresServer.SharedAsync()).NewDatabase("engine");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero));
        var fetcher = new FixtureFetcher(clock);
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock)
            .AddSingleton<IPageFetcher>(fetcher).AddSingleton<IFetchClientFactory, NoNetworkClients>()
            .AddSingleton<IChallengeDetector, NoChallenges>();
        services.AddHarvest().Configure(options => options.ConnectionString = connection);
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine().Configure(options => options.LeaseDuration = TimeSpan.FromSeconds(30));
        var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("engine-fixtures");
        return new(provider, connection, clock, fetcher);
    }

    internal async Task<(Guid TenantId, Guid ContainerId, Guid PipelineId, Guid SubscriptionId)> RegisterAsync(
        string host = "mstar.uz", SourceFamily family = SourceFamily.Site, IReadOnlyList<ScheduleDeclaration>? schedules = null,
        PipelineScope? pipelineScope = null, PipelineLimits? limits = null)
    {
        using var scope = Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Data(await registry.RegisterTenantAsync(new() { Slug = "tenant-" + Guid.NewGuid().ToString("N")[..8], Name = "Fixture consumer" }));
        var container = Data(await registry.RegisterContainerAsync(new() { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Data(await registry.RegisterSourceAsync(new()
        {
            ContainerId = container.Id, Family = family, Handle = host,
            Coverage = SourceCoverage.Whole, Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));
        var pipeline = Data(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = container.Id, SourceId = source.Id, Name = "Offline fixture", Schedules = schedules ?? [],
            Scope = pipelineScope, Limits = limits ?? new(),
        }));
        var subscription = Data(await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().SubscribeAsync(new()
        {
            TenantId = tenant.Id, ContainerId = container.Id, PipelineId = pipeline.Id,
            Schema = "wow2.event.v1", Backfill = BackfillMode.None,
        }));
        return (tenant.Id, container.Id, pipeline.Id, subscription.Id);
    }

    internal async Task<bool> ProcessAsync(CancellationToken cancellationToken = default)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<HarvestEngine>().ProcessNextAsync(cancellationToken);
    }

    internal async Task<int> DrainAsync()
    {
        var processed = 0;
        while (await ProcessAsync())
            if (++processed > 50) throw new InvalidOperationException("The fixture queue did not drain.");
        return processed;
    }

    internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("The fixture scalar returned no row.");
        return reader.GetFieldValue<T>(0);
    }

    internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    internal static T Data<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new InvalidOperationException(((Result<T>.Failure)result).Error.Message);

    public async ValueTask DisposeAsync() => await Services.DisposeAsync();

    internal sealed class FixtureFetcher(TimeProvider clock) : IPageFetcher
    {
        private readonly ConcurrentDictionary<string, Func<FetchRequest, CancellationToken, Task<FetchResponse>>> responses = new();
        internal ConcurrentQueue<string> Calls { get; } = [];

        internal void Set(string url, Func<FetchRequest, CancellationToken, Task<FetchResponse>> response) => responses[url] = response;
        internal void Set(string url, string body, string contentType = "text/html", int status = 200)
        {
            Set(url, (_, _) => Task.FromResult(Response(url, body, contentType, status)));
        }

        internal FetchResponse Response(string url, string body, string contentType = "text/html", int status = 200) => new()
        {
            Outcome = status is 404 or 410 ? FetchOutcome.Gone : FetchOutcome.Ok,
            Usage = new() { Requests = 1, Bytes = Encoding.UTF8.GetByteCount(body) },
            Document = new FetchedDocument
            {
                RequestUrl = new(url), FinalUrl = new(url), StatusCode = status, ContentType = contentType,
                Body = Encoding.UTF8.GetBytes(body), FetchedAtUtc = clock.GetUtcNow(),
            },
        };

        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue(request.Uri.AbsoluteUri);
            return responses.TryGetValue(request.Uri.AbsoluteUri, out var response) ? response(request, cancellationToken)
                : throw new InvalidOperationException("Unexpected fixture fetch: " + request.Uri.AbsoluteUri);
        }
    }

    private sealed class NoNetworkClients : IFetchClientFactory
    {
        public HttpClient CreateClient(CookieContainer? cookies = null) => new(new NoNetworkHandler());
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The fixture must not send real HTTP requests.");
    }

    private sealed class NoChallenges : IChallengeDetector
    {
        public string Key => "fixture";
        public ChallengeVerdict? Detect(ChallengeProbe probe) => null;
    }
}
