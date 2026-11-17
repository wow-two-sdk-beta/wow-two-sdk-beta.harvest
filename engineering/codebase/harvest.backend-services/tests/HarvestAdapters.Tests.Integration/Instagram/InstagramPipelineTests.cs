using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;
using WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

namespace HarvestAdapters.Tests.Integration.Instagram;

[Collection(SourceDeliveryCollection.Name)]
public sealed class InstagramPipelineTests
{
    private const string FakeToken = "synthetic-instagram-pipeline-token";
    private const string CursorA = "opaqueFixtureCursorA";
    private const string CursorB = "opaqueFixtureCursorB";

    [Fact]
    public async Task Real_provider_client_collects_cursor_only_pages_and_updates_generic_posts_without_duplicate_revisions()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Services.GetRequiredService<IInstagramClient>().Should().BeOfType<InstagramClient>();
        fixture.Services.GetRequiredService<IInstagramMediaBroker>().Should().BeOfType<InstagramMediaBroker>();

        RunModel firstRun = await fixture.CaptureAsync();

        firstRun.Status.Should().Be(RunStatus.Completed);
        firstRun.Requests.Should().Be(3);
        firstRun.Captured.Should().Be(2);
        firstRun.NewPosts.Should().Be(2);
        fixture.Http.Calls.Should().HaveCount(3);
        Uri.UnescapeDataString(fixture.Http.Calls[1].Uri.Query).Should().Contain($".after({CursorA})");
        Uri.UnescapeDataString(fixture.Http.Calls[2].Uri.Query).Should().Contain($".after({CursorB})");
        fixture.Http.Calls.Should().OnlyContain(call => call.Uri.Host == "graph.facebook.com"
            && call.Authorization == "Bearer " + FakeToken && !call.Uri.AbsoluteUri.Contains(FakeToken));
        var initial = await fixture.PostsAsync();
        initial.Should().HaveCount(2).And.OnlyContain(post => post.State == PostState.Live);
        initial.SelectMany(post => post.Links).Select(link => link.ExternalKey).Should().BeEquivalentTo("instagram:111", "instagram:222");
        var delivery = Value(await fixture.Services.GetRequiredService<IDeliveryBroker>().PullAsync(fixture.SubscriptionId));
        delivery.Data.Should().HaveCount(2).And.OnlyContain(item => item.Type == "post.new");

        fixture.Http.MediaSignature = "second-fetch";
        RunModel repeated = await fixture.CaptureAsync();

        repeated.Status.Should().Be(RunStatus.Completed);
        repeated.Captured.Should().Be(0);
        repeated.Unchanged.Should().Be(2);
        (await fixture.PostsAsync()).Select(post => (post.Id, post.Revision))
            .Should().BeEquivalentTo(initial.Select(post => (post.Id, post.Revision)));

        fixture.Http.FirstCaption = "Fixture publisher note one, with an edited detail.";
        RunModel edited = await fixture.CaptureAsync();

        edited.Status.Should().Be(RunStatus.Completed);
        edited.Captured.Should().Be(1);
        edited.Unchanged.Should().Be(1);
        var changed = await fixture.PostsAsync();
        changed.Should().HaveCount(2);
        PostModel changedPost = changed.Single(post => post.Links.Single().ExternalKey == "instagram:111");
        PostModel before = initial.Single(post => post.Id == changedPost.Id);
        changedPost.Revision.Should().Be(before.Revision + 1);
        changedPost.Base.Text.Should().Be(fixture.Http.FirstCaption);
        changed.Single(post => post.Links.Single().ExternalKey == "instagram:222").Revision
            .Should().Be(initial.Single(post => post.Links.Single().ExternalKey == "instagram:222").Revision);
        fixture.Http.Calls.Should().HaveCount(9);
        Value(await fixture.Services.GetRequiredService<IRegistrationBroker>().GetPipelineAsync(fixture.PipelineId))
            .Schedules.Should().BeEmpty();
        await fixture.AssertSanitizedAsync();
    }

    [Theory]
    [InlineData(190, FetchOutcome.LoginRequired)]
    [InlineData(4, FetchOutcome.RateLimited)]
    public async Task Provider_refusal_keeps_existing_posts_and_defers_the_host_without_an_immediate_retry(int code, FetchOutcome outcome)
    {
        await using var fixture = await Fixture.CreateAsync();
        (await fixture.CaptureAsync()).Status.Should().Be(RunStatus.Completed);
        var before = await fixture.PostsAsync();
        fixture.Http.ErrorCode = code;

        RunModel refused = await fixture.CaptureAsync();

        refused.Status.Should().Be(RunStatus.Partial);
        refused.CompletionReason.Should().Be(RunReason.SourceOutcome);
        refused.FetchOutcome.Should().Be(outcome);
        refused.Requests.Should().Be(1);
        refused.Failed.Should().Be(1);
        fixture.Http.Calls.Should().HaveCount(4, "the provider refusal must not trigger transport retries");
        (await fixture.Services.GetRequiredService<HarvestEngine>().ProcessNextAsync()).Should().BeFalse();
        (await fixture.Services.GetRequiredService<HarvestEngine>().ProcessNextAsync()).Should().BeFalse();
        fixture.Http.Calls.Should().HaveCount(4);
        var after = await fixture.PostsAsync();
        after.Should().OnlyContain(post => post.State == PostState.Live && post.Links.All(link => link.State == LinkState.Live));
        after.Select(post => (post.Id, post.Revision)).Should().BeEquivalentTo(before.Select(post => (post.Id, post.Revision)));
        var health = Value(await fixture.Services.GetRequiredService<IRunBroker>().HostsAsync(fixture.ContainerId));
        health.Should().ContainSingle().Which.PausedUntilUtc.Should().BeAfter(refused.FinishedAtUtc!.Value);
        if (code == 4)
            health.Single().PausedUntilUtc.Should().BeOnOrAfter(refused.FinishedAtUtc.Value.AddMinutes(20));
        await using var connection = new NpgsqlConnection(fixture.Database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT retry_at FROM harvest_step_attempts WHERE run_id=@run", connection);
        command.Parameters.AddWithValue("run", refused.Id);
        (await command.ExecuteScalarAsync()).Should().BeOfType<DateTime>();
        await fixture.AssertSanitizedAsync();
    }

    [Fact]
    public async Task Catalog_discovery_is_not_exposed_for_explicit_profile_pipelines()
    {
        await using var fixture = await Fixture.CreateAsync();

        var runs = fixture.Services.GetRequiredService<IRunBroker>();
        var started = Value(await runs.StartAsync(fixture.PipelineId, WorkKind.Discover));
        var refused = Value(await runs.GetAsync(started.Id));

        refused.Status.Should().Be(RunStatus.Failed);
        refused.CompletionReason.Should().Be(RunReason.SourceOutcome);
        refused.FetchOutcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        refused.Requests.Should().Be(0);
        (await fixture.Services.GetRequiredService<HarvestEngine>().ProcessNextAsync()).Should().BeFalse();
        fixture.Http.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Queued_run_keeps_its_profile_and_page_policy_when_the_saved_pipeline_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid queued = await fixture.QueueAsync();
        var registry = fixture.Services.GetRequiredService<IRegistrationBroker>();
        Value(await registry.UpdatePipelineAsync(fixture.PipelineId, new()
        {
            Name = "Changed after queueing",
            Scope = new() { Url = "https://www.instagram.com/another.publisher/" },
            Limits = new() { PageSize = 7, MaxPages = 1 }, Schedules = [],
        }));

        RunModel completed = await fixture.DrainAsync(queued);

        completed.Status.Should().Be(RunStatus.Completed);
        completed.Requests.Should().Be(3);
        completed.Captured.Should().Be(2);
        fixture.Http.Calls.Should().HaveCount(3).And.OnlyContain(call =>
            Uri.UnescapeDataString(call.Uri.Query).Contains("business_discovery.username(genericpublisher)", StringComparison.Ordinal)
            && Uri.UnescapeDataString(call.Uri.Query).Contains("media.limit(1)", StringComparison.Ordinal));
        PipelineModel saved = Value(await registry.GetPipelineAsync(fixture.PipelineId));
        saved.Scope.Url.Should().Be("https://www.instagram.com/another.publisher/");
        saved.Limits.PageSize.Should().Be(7);
        saved.Limits.MaxPages.Should().Be(1);
        await fixture.AssertSanitizedAsync();
    }

    [Fact]
    public async Task Saved_page_budget_stops_provider_pagination_without_a_host_config_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var registry = fixture.Services.GetRequiredService<IRegistrationBroker>();
        PipelineModel pipeline = Value(await registry.GetPipelineAsync(fixture.PipelineId));
        Value(await registry.UpdatePipelineAsync(pipeline.Id, new()
        {
            Scope = pipeline.Scope, Schedules = [], Limits = new() { PageSize = 1, MaxPages = 1 },
        }));

        RunModel run = await fixture.CaptureAsync();

        run.Requests.Should().Be(1);
        run.Captured.Should().Be(1);
        fixture.Http.Calls.Should().ContainSingle();
        (await fixture.Services.GetRequiredService<HarvestEngine>().ProcessNextAsync()).Should().BeFalse();
        (await fixture.PostsAsync()).Should().ContainSingle();
        await fixture.AssertSanitizedAsync();
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class Fixture(ServiceProvider provider, AsyncServiceScope scope, string database, FakeTimeProvider clock,
        ProviderHandler http, CapturedLogs logs, Guid containerId, Guid pipelineId, Guid subscriptionId) : IAsyncDisposable
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public string Database => database;
        public ProviderHandler Http => http;
        public Guid ContainerId => containerId;
        public Guid PipelineId => pipelineId;
        public Guid SubscriptionId => subscriptionId;

        public static async Task<Fixture> CreateAsync()
        {
            string database = await SourcePostgres.NewDatabaseAsync();
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero));
            var http = new ProviderHandler();
            var logs = new CapturedLogs();
            var services = new ServiceCollection().AddLogging(builder => builder.AddProvider(logs))
                .AddSingleton<TimeProvider>(clock);
            services.AddHarvest().Configure(options => options.ConnectionString = database);
            services.AddHarvestFetching();
            services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
            services.AddHarvestEngine();
            services.AddHarvestInstagram().Configure(options =>
            {
                options.AccessToken = FakeToken;
                options.ProfessionalAccountId = "123456789";
                options.ApiVersion = "v1.0";
            });
            // Only this fixture replaces outbound transport; the real registered client and SCFD brokers stay in use.
            services.AddHttpClient("WoW.Two.Integrations.Instagram").ConfigurePrimaryHttpMessageHandler(() => http);
            var provider = services.BuildServiceProvider(validateScopes: true);
            await provider.MigrateHarvestOnStartupAsync("instagram-provider-fixture");
            var scope = provider.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
            var tenant = Value(await registry.RegisterTenantAsync(new TenantRegistration { Slug = "generic-instagram", Name = "Generic Instagram consumer" }));
            var container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
            var source = Value(await registry.RegisterSourceAsync(new SourceRegistration
            {
                ContainerId = container.Id, Family = SourceFamily.Instagram, Handle = "www.instagram.com",
                Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
                AiAllowed = false, PoolShareable = false, CheckPolicy = "none",
            }));
            Value(await registry.ListPipelinesAsync(container.Id)).Should().BeEmpty();
            var pipeline = Value(await registry.DeclarePipelineAsync(new()
            {
                ContainerId = container.Id, SourceId = source.Id, Name = "Generic publisher",
                Scope = new() { Url = "https://www.instagram.com/genericpublisher/" },
                Limits = new() { PageSize = 1, MaxPages = 4 },
            }));
            pipeline.Schedules.Should().BeEmpty();
            var subscription = Value(await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().SubscribeAsync(new()
            {
                TenantId = tenant.Id, ContainerId = container.Id, PipelineId = pipeline.Id,
            }));
            http.Calls.Should().BeEmpty();
            return new(provider, scope, database, clock, http, logs, container.Id, pipeline.Id, subscription.Id);
        }

        public async Task<RunModel> CaptureAsync()
            => await DrainAsync(await QueueAsync());

        public async Task<Guid> QueueAsync()
        {
            clock.Advance(TimeSpan.FromHours(1));
            var runs = Services.GetRequiredService<IRunBroker>();
            return Value(await runs.StartAsync(pipelineId)).Id;
        }

        public async Task<RunModel> DrainAsync(Guid run)
        {
            for (int item = 0; item < 8; item++)
            {
                clock.Advance(TimeSpan.FromSeconds(6));
                if (!await Services.GetRequiredService<HarvestEngine>().ProcessNextAsync())
                    break;
            }
            return Value(await Services.GetRequiredService<IRunBroker>().GetAsync(run));
        }

        public async Task<IReadOnlyList<PostModel>> PostsAsync() =>
            Value(await Services.GetRequiredService<IPostBroker>().ListPostsAsync(containerId));

        public async Task AssertSanitizedAsync()
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT concat_ws(' ',request_url,final_url,warnings::text,convert_from(body_inline,'UTF8'))
                FROM harvest_captures WHERE container_id=@container
                UNION ALL
                SELECT concat_ws(' ',a.message,a.operations::text)
                FROM harvest_step_attempts a JOIN harvest_runs r ON r.id=a.run_id WHERE r.container_id=@container
                UNION ALL
                SELECT coalesce(message,'') FROM harvest_runs WHERE container_id=@container
                """, connection);
            command.Parameters.AddWithValue("container", containerId);
            await using var reader = await command.ExecuteReaderAsync();
            var persisted = new StringBuilder(JsonSerializer.Serialize(await PostsAsync()));
            while (await reader.ReadAsync())
                persisted.AppendLine(reader.GetString(0));
            string combined = persisted.ToString() + string.Join('\n', logs.Messages);
            combined.Should().NotContain(FakeToken).And.NotContain(CursorA).And.NotContain(CursorB)
                .And.NotContain("?fields=").And.NotContain("access_token=");
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public List<(Uri Uri, string? Authorization)> Calls { get; } = [];
        public string FirstCaption { get; set; } = "Fixture publisher note one.";
        public string MediaSignature { get; set; } = "first-fetch";
        public int? ErrorCode { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            if (ErrorCode is { } code)
            {
                var response = Response(JsonSerializer.Serialize(new { error = new { code, message = FakeToken + " " + CursorA } }), HttpStatusCode.BadRequest);
                if (code == 4)
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(20));
                return Task.FromResult(response);
            }
            string query = Uri.UnescapeDataString(request.RequestUri!.Query);
            if (query.Contains($".after({CursorB})", StringComparison.Ordinal))
                return Task.FromResult(Response("""{"business_discovery":{"username":"genericpublisher","media":{"data":[]}}}"""));
            bool second = query.Contains($".after({CursorA})", StringComparison.Ordinal);
            string id = second ? "222" : "111";
            string body = JsonSerializer.Serialize(new
            {
                business_discovery = new
                {
                    username = "genericpublisher",
                    media = new
                    {
                        data = new[] { new
                        {
                            id, caption = second ? "Fixture publisher note two." : FirstCaption,
                            timestamp = "2026-11-16T12:00:00+0000", media_type = "IMAGE",
                            permalink = "https://www.instagram.com/p/" + (second ? "FixtureTwo" : "FixtureOne") + "/",
                            media_url = $"https://images.cdninstagram.com/{id}.jpg?signature={MediaSignature}",
                        } },
                        paging = new { cursors = new { after = second ? CursorB : CursorA } },
                    },
                },
            });
            return Task.FromResult(Response(body));
        }

        private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(Messages);
        public void Dispose() { }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
