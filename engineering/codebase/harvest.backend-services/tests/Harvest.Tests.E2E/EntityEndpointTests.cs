using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Harvest.Tests.E2E;

/// <summary>The entity routes through the real API: <c>posts:read</c> gates them, a tenant reads the pool and its own
/// containers only, and each route answers its page shape.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class EntityEndpointTests(HarvestApiFixture fixture)
{
    private const string Container = "00000000-0000-0000-0000-000000000001";

    [Theory]
    [InlineData("/api/entities?containerId=" + Container + "&q=fashion")]
    [InlineData("/api/entities/00000000-0000-0000-0000-000000000002?containerId=" + Container)]
    [InlineData("/api/entities/00000000-0000-0000-0000-000000000002/posts?containerId=" + Container)]
    [InlineData("/api/entities/calendar?containerId=" + Container + "&from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/entities/events?containerId=" + Container)]
    public async Task EntityRoutes_ShouldRequireAKey_AndPostsRead(string path)
    {
        using var anonymous = fixture.CreateClient();
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var tenant = await TenantAsync(operatorClient);
        using var runner = fixture.CreateClient(await KeyAsync(operatorClient, tenant, "pipelines:read", "events:read"));

        await ExpectAsync(anonymous.GetAsync(path), HttpStatusCode.Unauthorized);
        await ExpectAsync(runner.GetAsync(path), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EntityRoutes_ShouldServeOwnAndPoolEntities_AndHideAnotherTenants()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await LaneAsync(operatorClient, await TenantAsync(operatorClient));
        var foreign = await LaneAsync(operatorClient, await TenantAsync(operatorClient));
        var ownEntity = await SeedAsync(operatorClient, own, "Visa Fashion Week Tashkent");
        var foreignEntity = await SeedAsync(operatorClient, foreign, "Visa Fashion Week Tashkent");
        using var reader = fixture.CreateClient(await KeyAsync(operatorClient, own.TenantId, "posts:read"));

        var hits = await ReadAsync(reader.GetAsync($"/api/entities?containerId={own.ContainerId}&q=Visa%20Fashion%20Week&type=event"));
        hits.EnumerateArray().Select(hit => hit.GetProperty("id").GetGuid()).Should().Equal(ownEntity.Id);
        hits[0].GetProperty("years").EnumerateArray().Select(year => (year.GetProperty("year").GetInt32(), year.GetProperty("predicted").GetBoolean()))
            .Should().Equal((2025, false), (2026, true));

        var timeline = await ReadAsync(reader.GetAsync($"/api/entities/{ownEntity.Id}?containerId={own.ContainerId}"));
        timeline.GetProperty("name").GetString().Should().Be("Visa Fashion Week Tashkent");
        timeline.GetProperty("years")[0].GetProperty("occurrences")[0].GetProperty("place").GetProperty("city").GetString().Should().Be("Tashkent");

        var posts = await ReadAsync(reader.GetAsync($"/api/entities/{ownEntity.Id}/posts?containerId={own.ContainerId}&year=2025&limit=1"));
        posts.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        var post = posts.GetProperty("data").EnumerateArray().Should().ContainSingle().Which;
        (post.GetProperty("postId").GetGuid(), post.GetProperty("title").GetString(), post.GetProperty("source").GetString())
            .Should().Be((ownEntity.PostId, "Visa Fashion Week opens", own.Host));
        post.TryGetProperty("text", out _).Should().BeFalse("an entity's posts carry titles, never text");

        var calendar = await ReadAsync(reader.GetAsync($"/api/entities/calendar?containerId={own.ContainerId}&from=2026-01-01&to=2026-12-31"));
        calendar.EnumerateArray().Should().ContainSingle().Which.GetProperty("occurrence").GetProperty("predicted").GetBoolean().Should().BeTrue();

        var events = await PollAsync(reader, $"/api/entities/events?containerId={own.ContainerId}&limit=1");
        events.GetProperty("data")[0].GetProperty("type").GetString().Should().Be("entity.created");
        events.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        var next = events.GetProperty("next").GetString()!;
        var rest = await ReadAsync(reader.GetAsync($"/api/entities/events?containerId={own.ContainerId}&cursor={Uri.EscapeDataString(next)}"));
        rest.GetProperty("data").EnumerateArray().Select(change => change.GetProperty("type").GetString()).Should().Equal("occurrence.added");

        foreach (var path in new[]
                 {
                     $"/api/entities?containerId={foreign.ContainerId}&q=Visa", $"/api/entities/{foreignEntity.Id}?containerId={foreign.ContainerId}",
                     $"/api/entities/{foreignEntity.Id}/posts?containerId={foreign.ContainerId}",
                     $"/api/entities/calendar?containerId={foreign.ContainerId}&from=2026-01-01&to=2026-12-31",
                     $"/api/entities/events?containerId={foreign.ContainerId}", $"/api/entities/{foreignEntity.Id}?containerId={own.ContainerId}",
                 })
            await ExpectAsync(reader.GetAsync(path), HttpStatusCode.NotFound);
        await ExpectAsync(operatorClient.GetAsync($"/api/entities/{foreignEntity.Id}?containerId={foreign.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(reader.GetAsync($"/api/entities/calendar?containerId={own.ContainerId}&from=2026-01-01"), HttpStatusCode.BadRequest);
        await ExpectAsync(reader.GetAsync($"/api/entities/events?containerId={own.ContainerId}&cursor=AAAA"), HttpStatusCode.BadRequest);
        await ExpectAsync(reader.GetAsync($"/api/entities?containerId={own.ContainerId}&type=news"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EntityRoutes_ShouldLetATenantReadThePool()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var pool = await PoolLaneAsync(operatorClient);
        var entity = await SeedAsync(operatorClient, pool, "Pool Craft Fair " + Guid.NewGuid().ToString("N")[..6]);
        using var reader = fixture.CreateClient(await KeyAsync(operatorClient, await TenantAsync(operatorClient), "posts:read"));

        await ExpectAsync(reader.GetAsync($"/api/entities/{entity.Id}?containerId={pool.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(reader.GetAsync($"/api/entities/events?containerId={pool.ContainerId}"), HttpStatusCode.OK);
    }

    /// <summary>Writes one event with a held 2025 run and a predicted 2026 run, a bound post and its two creation events,
    /// as resolution would.</summary>
    private async Task<(Guid Id, Guid PostId)> SeedAsync(HttpClient operatorClient, Lane lane, string name)
    {
        var postId = await CaptureAsync(operatorClient, lane, "Visa Fashion Week opens");
        Guid entity = Guid.NewGuid(), held = Guid.NewGuid(), predicted = Guid.NewGuid();
        var database = fixture.Host.Services.GetRequiredService<IConfiguration>()["DatabaseSettings:ConnectionString"];
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO harvest_entities(id,container_id,entity_type,name,name_key,primary_domain,cadence,first_seen_at,last_seen_at,created_at,updated_at)
            VALUES(@entity,@container,'event',@name,lower(@name),'style-fashion','annual',now(),now(),now(),now());
            INSERT INTO harvest_entity_occurrences(id,entity_id,label,starts,ends,precision,certainty,predicted,status,place,created_at,updated_at)
            VALUES(@held,@entity,'2025','2025-10-20','2025-10-25','day','stated',false,'held','{"city":"Tashkent"}',now(),now()),
                  (@predicted,@entity,'2026','2026-10-19','2026-10-24','day','estimated',true,'scheduled',NULL,now(),now());
            INSERT INTO harvest_entity_relations(post_id,entity_id,container_id,content_hash,use_case,occurrence_id,role,confidence,created_at,updated_at)
            VALUES(@post,@entity,@container,sha256('seed'),'extract-event',@held,'announcement',1,now(),now());
            INSERT INTO harvest_entity_events(event_id,container_id,entity_id,entity_type,domain,type,occurrence_id,occurred_at,payload)
            VALUES(gen_random_uuid(),@container,@entity,'event','style-fashion','entity.created',NULL,now(),
                       jsonb_build_object('name',@name,'status','active','cadence','annual')),
                  (gen_random_uuid(),@container,@entity,'event','style-fashion','occurrence.added',@held,now(),
                       jsonb_build_object('name',@name,'status','active','cadence','annual'));
            """, connection);
        command.Parameters.AddWithValue("entity", entity);
        command.Parameters.AddWithValue("container", Guid.Parse(lane.ContainerId));
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("held", held);
        command.Parameters.AddWithValue("predicted", predicted);
        command.Parameters.AddWithValue("post", postId);
        await command.ExecuteNonQueryAsync();
        return (entity, postId);
    }

    private static async Task<string> TenantAsync(HttpClient client) =>
        (await DataAsync(PostAsync(client, "/api/tenants", new { slug = "entities-" + Unique(), name = "Entity reader" }))).GetProperty("id").GetString()!;

    private static async Task<Lane> LaneAsync(HttpClient client, string tenantId)
    {
        var container = await DataAsync(PostAsync(client, "/api/containers", new { kind = "private", tenantId }));
        return await SourceAsync(client, tenantId, container.GetProperty("id").GetString()!);
    }

    private static async Task<Lane> PoolLaneAsync(HttpClient client)
    {
        using var response = await PostAsync(client, "/api/containers", new { kind = "pool" });
        string container;
        if (response.IsSuccessStatusCode)
            container = (await DataOfAsync(response)).GetProperty("id").GetString()!;
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var listed = await DataAsync(client.GetAsync("/api/containers"));
            container = listed.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "pool").GetProperty("id").GetString()!;
        }
        return await SourceAsync(client, string.Empty, container);
    }

    private static async Task<Lane> SourceAsync(HttpClient client, string tenantId, string containerId)
    {
        var host = "entities-" + Unique() + ".example";
        var source = await DataAsync(PostAsync(client, "/api/sources", new
        {
            containerId, family = "site", handle = host, coverage = "whole", completeness = "complete", posture = "ownFetch",
        }));
        return new Lane(tenantId, containerId, source.GetProperty("id").GetString()!, host);
    }

    private static async Task<string> KeyAsync(HttpClient client, string tenant, params string[] scopes) =>
        (await DataAsync(PostAsync(client, $"/api/tenants/{tenant}/api-keys", new { name = "entities-fixture", scopes })))
            .GetProperty("secret").GetString()!;

    private static async Task<Guid> CaptureAsync(HttpClient client, Lane lane, string title)
    {
        var url = "https://" + lane.Host + "/" + Unique();
        var result = await DataAsync(PostAsync(client, "/api/posts/captures", new
        {
            containerId = lane.ContainerId, sourceId = lane.SourceId,
            capture = new
            {
                externalKey = url, url, requestUrl = url, finalUrl = url, httpStatus = 200, capturedAtUtc = DateTimeOffset.UtcNow,
                body = Convert.ToBase64String(Encoding.UTF8.GetBytes(title)), contentProjection = title, contentType = "text/plain",
            },
            envelope = new
            {
                schema = "wow2.post.v1", kind = "event", title, text = "Private body text of the post",
                extraction = new { normVersion = "norm@1", rulesVersion = "rules@1", tiersRun = new[] { "deterministic", "rules" } },
            },
        }));
        return result.GetProperty("post").GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        client.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    private static async Task ExpectAsync(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> DataAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        return await DataOfAsync(response);
    }

    private static async Task<JsonElement> DataOfAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("data").Clone();
    }

    /// <summary>Reads an entity route's data, which never holds post text.</summary>
    private static async Task<JsonElement> ReadAsync(Task<HttpResponseMessage> request)
    {
        using var response = await request;
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Private body text", "entity reads never return post text");
        return await DataOfAsync(response);
    }

    /// <summary>Reads a feed page until it holds an event: the feed shows rows below the snapshot horizon only.</summary>
    private static async Task<JsonElement> PollAsync(HttpClient client, string path)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = await ReadAsync(client.GetAsync(path));
            if (page.GetProperty("data").GetArrayLength() > 0 || elapsed.Elapsed > TimeSpan.FromSeconds(5))
                return page;
            await Task.Delay(50);
        }
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private sealed record Lane(string TenantId, string ContainerId, string SourceId, string Host);
}
