using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.E2E;

/// <summary>Checks who can read processor evidence and which saved capture each result describes.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class PostProcessingEndpointTests(HarvestApiFixture fixture)
{
    [Fact]
    public async Task Processing_ShouldRequirePostReadScopeAndTheOwningContainer()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var own = await LaneAsync(scope.ServiceProvider);
        var other = await LaneAsync(scope.ServiceProvider);
        var at = DateTimeOffset.UtcNow;
        var ownCapture = await CaptureAsync(scope.ServiceProvider, own, at, "Own listing");
        var otherCapture = await CaptureAsync(scope.ServiceProvider, other, at, "Foreign listing");
        await SeedResultAsync(own, ownCapture, at, at.AddSeconds(1), "own evidence");
        await SeedResultAsync(other, otherCapture, at, at.AddSeconds(1), "foreign evidence");
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        using var anonymous = fixture.CreateClient();
        using var reader = fixture.CreateClient(await KeyAsync(admin, own.TenantId, "posts:read"));
        using var narrow = fixture.CreateClient(await KeyAsync(admin, own.TenantId, "events:read"));

        var ownPath = Path(own.ContainerId, ownCapture.Post.Id);
        var foreignPath = Path(other.ContainerId, otherCapture.Post.Id);
        await ExpectAsync(anonymous, ownPath, HttpStatusCode.Unauthorized);
        await ExpectAsync(narrow, ownPath, HttpStatusCode.Forbidden);
        await ExpectAsync(reader, foreignPath, HttpStatusCode.NotFound);
        await ExpectAsync(reader, Path(own.ContainerId, otherCapture.Post.Id), HttpStatusCode.NotFound);

        var result = (await ResultsAsync(reader, ownPath)).Single();
        result.GetProperty("sourceId").GetGuid().Should().Be(own.SourceId);
        result.GetProperty("output").GetProperty("evidence").GetString().Should().Be("own evidence");
        (await ResultsAsync(admin, foreignPath)).Single().GetProperty("sourceId").GetGuid().Should().Be(other.SourceId);
    }

    [Fact]
    public async Task Processing_ShouldHideResultEvidenceFromNonShareablePoolSources()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var readerLane = await LaneAsync(scope.ServiceProvider);
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var containers = (await registry.ListContainersAsync(null)).ValueOrThrow();
        var pool = containers.SingleOrDefault(container => container.Kind == ContainerKind.Pool)
            ?? (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var shared = await SourceLaneAsync(registry, readerLane.TenantId, pool.Id, poolShareable: true);
        var restricted = await SourceLaneAsync(registry, readerLane.TenantId, pool.Id, poolShareable: false);
        var origin = $"urn:listing:{Guid.NewGuid():N}";
        var at = DateTimeOffset.UtcNow;
        var sharedCapture = await CaptureAsync(scope.ServiceProvider, shared, at, "Shared evidence source", origin);
        var restrictedCapture = await CaptureAsync(scope.ServiceProvider, restricted, at, "Restricted evidence source", origin);
        restrictedCapture.Post.Id.Should().Be(sharedCapture.Post.Id);
        await SeedResultAsync(shared, sharedCapture, at, at.AddSeconds(1), "shared evidence");
        await SeedResultAsync(restricted, restrictedCapture, at, at.AddSeconds(2), "restricted evidence");
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        using var reader = fixture.CreateClient(await KeyAsync(admin, readerLane.TenantId, "posts:read"));
        var path = Path(pool.Id, sharedCapture.Post.Id);

        var permitted = (await ResultsAsync(reader, path)).Single();
        permitted.GetProperty("sourceId").GetGuid().Should().Be(shared.SourceId);
        permitted.GetProperty("output").GetProperty("evidence").GetString().Should().Be("shared evidence");
        (await ResultsAsync(admin, path)).Select(result => result.GetProperty("sourceId").GetGuid())
            .Should().Equal(restricted.SourceId, shared.SourceId);
    }

    [Fact]
    public async Task Processing_ShouldFilterTheSourceBeforeLimitingMergedPostResults()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var first = await LaneAsync(scope.ServiceProvider);
        var second = await SourceLaneAsync(registry, first.TenantId, first.ContainerId, poolShareable: true);
        var origin = $"urn:listing:{Guid.NewGuid():N}";
        var at = DateTimeOffset.UtcNow;
        var firstCapture = await CaptureAsync(scope.ServiceProvider, first, at, "First source", origin);
        var secondCapture = await CaptureAsync(scope.ServiceProvider, second, at, "Second source", origin);
        secondCapture.Post.Id.Should().Be(firstCapture.Post.Id);
        var firstRun = await SeedResultAsync(first, firstCapture, at, at.AddSeconds(1), "first evidence");
        var secondRun = await SeedResultAsync(second, secondCapture, at, at.AddSeconds(2), "second evidence");
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var path = Path(first.ContainerId, firstCapture.Post.Id) + "&limit=1";

        (await ResultsAsync(admin, path)).Single().GetProperty("runId").GetGuid().Should().Be(secondRun);
        (await ResultsAsync(admin, path + $"&sourceId={first.SourceId}")).Single()
            .GetProperty("runId").GetGuid().Should().Be(firstRun);
        (await ResultsAsync(admin, path + $"&sourceId={Guid.NewGuid()}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Processing_ShouldReturnNewestResultsAndIdentifyTheCurrentCapture()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var lane = await LaneAsync(scope.ServiceProvider);
        var at = DateTimeOffset.UtcNow;
        var older = await CaptureAsync(scope.ServiceProvider, lane, at, "Original listing description");
        var olderRun = await SeedResultAsync(lane, older, at, at.AddSeconds(1), "original evidence");
        var current = await CaptureAsync(scope.ServiceProvider, lane, at.AddMinutes(1), "Edited listing description");
        current.Post.Id.Should().Be(older.Post.Id);
        var currentRun = await SeedResultAsync(lane, current, at.AddMinutes(1), at.AddMinutes(2), "current evidence");
        var pendingRun = await SeedResultAsync(lane, current, at.AddMinutes(3), null, null, "rental-terms@next");
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var path = Path(lane.ContainerId, current.Post.Id);

        var results = await ResultsAsync(admin, path);
        results.Select(result => result.GetProperty("runId").GetGuid()).Should().Equal(pendingRun, currentRun, olderRun);
        results.Select(result => result.GetProperty("captureId").GetGuid()).Should()
            .Equal(current.CaptureId!.Value, current.CaptureId.Value, older.CaptureId!.Value);
        results.Select(result => result.GetProperty("isCurrent").GetBoolean()).Should().Equal(true, true, false);
        results.Select(result => result.GetProperty("runStatus").GetString()).Should().Equal("queued", "completed", "completed");
        results[1].GetProperty("output").GetProperty("evidence").GetString().Should().Be("current evidence");
        results[2].GetProperty("output").GetProperty("evidence").GetString().Should().Be("original evidence");

        (await ResultsAsync(admin, path + "&limit=1")).Single().GetProperty("runId").GetGuid().Should().Be(pendingRun);
        (await ResultsAsync(admin, path + "&limit=100")).Should().HaveCount(3);
        await ExpectAsync(admin, path + "&limit=0", HttpStatusCode.BadRequest);
        await ExpectAsync(admin, path + "&limit=101", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Processing_ShouldHideRetainedResultsAfterAPostIsPurged()
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var lane = await LaneAsync(scope.ServiceProvider);
        var at = DateTimeOffset.UtcNow;
        var capture = await CaptureAsync(scope.ServiceProvider, lane, at, "Listing to purge");
        await SeedResultAsync(lane, capture, at, at.AddSeconds(1), "retained evidence");
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var path = Path(lane.ContainerId, capture.Post.Id);
        (await ResultsAsync(admin, path)).Should().ContainSingle();

        // Set the tombstone while leaving a stale result in place to exercise the read boundary.
        await using var connection = await OpenDatabaseAsync();
        await using var purge = new NpgsqlCommand("UPDATE harvest_posts SET purged_at=@at WHERE id=@post", connection);
        purge.Parameters.AddWithValue("at", at.AddMinutes(1));
        purge.Parameters.AddWithValue("post", capture.Post.Id);
        (await purge.ExecuteNonQueryAsync()).Should().Be(1);

        await ExpectAsync(admin, path, HttpStatusCode.NotFound);
    }

    private static async Task<Lane> LaneAsync(IServiceProvider services)
    {
        var registry = services.GetRequiredService<IRegistrationBroker>();
        var slug = "processing-" + Guid.NewGuid().ToString("N")[..12];
        var tenant = (await registry.RegisterTenantAsync(new TenantRegistration { Slug = slug, Name = "Processing fixture" })).ValueOrThrow();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id })).ValueOrThrow();
        return await SourceLaneAsync(registry, tenant.Id, container.Id, poolShareable: true);
    }

    private static async Task<Lane> SourceLaneAsync(IRegistrationBroker registry, Guid tenantId, Guid containerId, bool poolShareable)
    {
        var host = $"processing-{Guid.NewGuid():N}.example";
        var source = (await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = containerId, Family = SourceFamily.Site, Handle = host, Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, PoolShareable = poolShareable,
        })).ValueOrThrow();
        var pipeline = (await registry.DeclarePipelineAsync(new PipelineDeclaration { ContainerId = containerId, SourceId = source.Id })).ValueOrThrow();
        return new(tenantId, containerId, source.Id, pipeline.Id, host);
    }

    private static async Task<CaptureSaveResult> CaptureAsync(IServiceProvider services, Lane lane, DateTimeOffset at, string text,
        string? originKey = null)
    {
        var url = $"https://{lane.Host}/listing";
        var capture = new Capture
        {
            ExternalKey = url, Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = at, HttpStatus = 200,
            Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, ContentType = "text/plain", OriginKey = originKey,
        };
        var envelope = new PostEnvelope
        {
            Title = text, Text = text, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        var posts = services.GetRequiredService<IPostBroker>();
        return (await posts.SaveCaptureAsync(lane.ContainerId, lane.SourceId, null, capture, envelope)).ValueOrThrow();
    }

    private async Task<Guid> SeedResultAsync(Lane lane, CaptureSaveResult capture, DateTimeOffset queuedAt,
        DateTimeOffset? processedAt, string? evidence, string processor = "rental-terms@fixture")
    {
        await using var connection = await OpenDatabaseAsync();
        await using var seed = new NpgsqlCommand("""
            WITH run AS (
                INSERT INTO harvest_runs(id,container_id,pipeline_id,kind,trigger,scheduled_for,queued_at,status)
                VALUES(@run,@container,@pipeline,'process','manual',@queued,@queued,@status)
                RETURNING id
            )
            INSERT INTO harvest_processing_results(capture_id,processor,container_id,link_id,run_id,next_ready_at,processed_at,result)
            SELECT @capture,@processor,@container,@link,id,@queued,@processed,@output FROM run
            """, connection);
        var runId = Guid.NewGuid();
        seed.Parameters.AddWithValue("run", runId);
        seed.Parameters.AddWithValue("container", lane.ContainerId);
        seed.Parameters.AddWithValue("pipeline", lane.PipelineId);
        seed.Parameters.AddWithValue("capture", capture.CaptureId!.Value);
        seed.Parameters.AddWithValue("link", capture.LinkId);
        seed.Parameters.AddWithValue("processor", processor);
        seed.Parameters.AddWithValue("queued", queuedAt);
        seed.Parameters.AddWithValue("status", processedAt is null ? "queued" : "completed");
        seed.Parameters.AddWithValue("processed", NpgsqlDbType.TimestampTz, (object?)processedAt ?? DBNull.Value);
        seed.Parameters.AddWithValue("output", NpgsqlDbType.Jsonb,
            evidence is null ? DBNull.Value : JsonSerializer.Serialize(new { schema = "fixture.v1", evidence }));
        (await seed.ExecuteNonQueryAsync()).Should().Be(1);
        return runId;
    }

    private async Task<NpgsqlConnection> OpenDatabaseAsync()
    {
        var configuration = fixture.Host.Services.GetRequiredService<IConfiguration>();
        var connection = new NpgsqlConnection(configuration["DatabaseSettings:ConnectionString"]);
        await connection.OpenAsync();
        return connection;
    }

    private static string Path(Guid containerId, Guid postId) => $"/api/posts/{postId}/processing?containerId={containerId}";

    private static async Task<string> KeyAsync(HttpClient admin, Guid tenantId, string scope)
    {
        using var response = await admin.PostAsJsonAsync($"/api/tenants/{tenantId}/api-keys",
            new { name = "processing-fixture", scopes = new[] { scope } });
        return (await DataAsync(response, HttpStatusCode.Created)).GetProperty("secret").GetString()!;
    }

    private static async Task<JsonElement[]> ResultsAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        return (await DataAsync(response)).EnumerateArray().ToArray();
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode status)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
    }

    private sealed record Lane(Guid TenantId, Guid ContainerId, Guid SourceId, Guid PipelineId, string Host);
}
