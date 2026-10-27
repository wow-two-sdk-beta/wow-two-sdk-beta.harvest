using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Harvest.Tests.E2E;

/// <summary>Checks engine, post and delivery isolation through the real API with background fetching disabled.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class EngineAccessTests(HarvestApiFixture fixture)
{
    private static readonly string[] TenantScopes =
    ["pipelines:read", "pipelines:run", "pipelines:manage", "posts:read", "subscriptions:manage", "events:read"];

    [Theory]
    [InlineData("GET", "/api/runs?pipelineId=00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/runs")]
    [InlineData("GET", "/api/posts?containerId=00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/hosts?containerId=00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/subscriptions")]
    [InlineData("GET", "/api/subscriptions/00000000-0000-0000-0000-000000000001/events")]
    public async Task EngineRoutes_ShouldRequireAnAuthenticatedApiKey(string method, string path)
    {
        using var client = fixture.CreateClient();
        await ExpectAsync(method == "POST" ? PostAsync(client, path, new { }) : client.GetAsync(path), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Runs_ShouldAllowOwnControlsAndHideForeignRunsAndHostQueues()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await LaneAsync(operatorClient);
        var other = await LaneAsync(operatorClient);
        var pooled = await PoolLaneAsync(operatorClient);
        using var client = fixture.CreateClient(await KeyAsync(operatorClient, own.TenantId, TenantScopes));
        var ownRun = await DataAsync(await PostAsync(client, "/api/runs", new { pipelineId = own.PipelineId }));
        var otherRun = await DataAsync(await PostAsync(operatorClient, "/api/runs", new { pipelineId = other.PipelineId }));
        var ownRunId = ownRun.GetProperty("id").GetString();
        var otherRunId = otherRun.GetProperty("id").GetString();

        await ExpectAsync(client.GetAsync($"/api/runs/{ownRunId}"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/runs?pipelineId={own.PipelineId}"), HttpStatusCode.OK);
        var hosts = await DataAsync(await client.GetAsync($"/api/hosts?containerId={own.ContainerId}"));
        hosts.EnumerateArray().Select(host => host.GetProperty("hostKey").GetString()).Should().Contain(own.Host).And.NotContain(other.Host);
        await ExpectAsync(client.GetAsync($"/api/runs/{otherRunId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/runs?pipelineId={other.PipelineId}"), HttpStatusCode.NotFound);
        await ExpectAsync(PostAsync(client, "/api/runs", new { pipelineId = other.PipelineId }), HttpStatusCode.NotFound);
        await ExpectAsync(PostAsync(client, $"/api/runs/{otherRunId}/cancel", new { }), HttpStatusCode.NotFound);
        await ExpectAsync(PostAsync(client, $"/api/pipelines/{other.PipelineId}/pause", new { paused = true }), HttpStatusCode.NotFound);
        await ExpectAsync(PostAsync(client, $"/api/pipelines/{pooled.PipelineId}/pause", new { paused = true }), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/hosts?containerId={other.ContainerId}"), HttpStatusCode.NotFound);
        var foreignAfter = await DataAsync(await operatorClient.GetAsync($"/api/runs/{otherRunId}"));
        foreignAfter.GetProperty("status").GetString().Should().Be("queued");

        var cancelled = await DataAsync(await PostAsync(client, $"/api/runs/{ownRunId}/cancel", new { }));
        cancelled.GetProperty("status").GetString().Should().Be("cancelled");
        var paused = await DataAsync(await PostAsync(client, $"/api/pipelines/{own.PipelineId}/pause", new { paused = true }));
        paused.GetProperty("state").GetString().Should().Be("paused");
    }

    [Fact]
    public async Task Posts_ShouldRequireTheOwningContainerAndKeepIngestionOperatorOnly()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await LaneAsync(operatorClient);
        var other = await LaneAsync(operatorClient);
        var ownPost = await CaptureAsync(operatorClient, own, "Own fixture");
        var otherPost = await CaptureAsync(operatorClient, other, "Other fixture");
        using var client = fixture.CreateClient(await KeyAsync(operatorClient, own.TenantId, TenantScopes));

        var posts = await DataAsync(await client.GetAsync($"/api/posts?containerId={own.ContainerId}"));
        posts.EnumerateArray().Select(post => post.GetProperty("id").GetString()).Should().Equal(ownPost);
        await ExpectAsync(client.GetAsync($"/api/posts/{ownPost}?containerId={own.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/posts?containerId={other.ContainerId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/posts/{otherPost}?containerId={other.ContainerId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/posts/{otherPost}?containerId={own.ContainerId}"), HttpStatusCode.NotFound);
        await ExpectAsync(PostAsync(client, "/api/posts/captures", new { }), HttpStatusCode.Forbidden);
        await ExpectAsync(PostAsync(client, "/api/posts/observations", new { }), HttpStatusCode.Forbidden);
        await ExpectAsync(PostAsync(client, "/api/post-merges", new { }), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Subscriptions_ShouldBindTheCallerToItsOwnTenantAndCursor()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await LaneAsync(operatorClient);
        var other = await LaneAsync(operatorClient);
        using var client = fixture.CreateClient(await KeyAsync(operatorClient, own.TenantId, TenantScopes));
        var ownSubscription = await DataAsync(await PostAsync(client, "/api/subscriptions", Subscription(own)));
        var otherSubscription = await DataAsync(await PostAsync(operatorClient, "/api/subscriptions", Subscription(other)));
        var ownId = ownSubscription.GetProperty("id").GetString();
        var foreignId = otherSubscription.GetProperty("id").GetString();

        await ExpectAsync(PostAsync(client, "/api/subscriptions", Subscription(other)), HttpStatusCode.NotFound);
        await ExpectAsync(PostAsync(client, "/api/subscriptions", new
        {
            tenantId = own.TenantId, containerId = other.ContainerId, pipelineId = other.PipelineId, backfill = "none",
        }), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/subscriptions/{foreignId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/subscriptions/{foreignId}/events"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/subscriptions?tenantId={other.TenantId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/subscriptions/{ownId}"), HttpStatusCode.OK);
        var page = await DataAsync(await client.GetAsync($"/api/subscriptions/{ownId}/events"));
        page.GetProperty("data").GetArrayLength().Should().Be(0);
        page.GetProperty("next").GetString().Should().NotBeNullOrEmpty();
        var listed = await DataAsync(await client.GetAsync("/api/subscriptions"));
        listed.EnumerateArray().Select(subscription => subscription.GetProperty("id").GetString()).Should().Contain(ownId).And.NotContain(foreignId);
    }

    [Fact]
    public async Task Keys_ShouldRequireEachEngineRouteScopeEvenInsideTheirOwnContainer()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await LaneAsync(operatorClient);
        using var reader = fixture.CreateClient(await KeyAsync(operatorClient, own.TenantId, "posts:read"));

        await ExpectAsync(reader.GetAsync($"/api/posts?containerId={own.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(reader.GetAsync($"/api/runs?pipelineId={own.PipelineId}"), HttpStatusCode.Forbidden);
        await ExpectAsync(PostAsync(reader, "/api/runs", new { pipelineId = own.PipelineId }), HttpStatusCode.Forbidden);
        await ExpectAsync(PostAsync(reader, $"/api/pipelines/{own.PipelineId}/pause", new { paused = true }), HttpStatusCode.Forbidden);
        await ExpectAsync(reader.GetAsync($"/api/hosts?containerId={own.ContainerId}"), HttpStatusCode.Forbidden);
        await ExpectAsync(PostAsync(reader, "/api/subscriptions", Subscription(own)), HttpStatusCode.Forbidden);
        await ExpectAsync(reader.GetAsync("/api/subscriptions"), HttpStatusCode.Forbidden);
        await ExpectAsync(reader.GetAsync($"/api/subscriptions/{Guid.NewGuid()}/events"), HttpStatusCode.Forbidden);
    }

    private static async Task<Lane> LaneAsync(HttpClient client)
    {
        var tenant = await DataAsync(await PostAsync(client, "/api/tenants", new { slug = "access-" + Unique(), name = "Fixture tenant" }));
        var tenantId = tenant.GetProperty("id").GetString()!;
        var container = await DataAsync(await PostAsync(client, "/api/containers", new { kind = "private", tenantId }));
        return await PipelineAsync(client, tenantId, container.GetProperty("id").GetString()!);
    }

    private static async Task<Lane> PoolLaneAsync(HttpClient client)
    {
        using var response = await PostAsync(client, "/api/containers", new { kind = "pool" });
        string container;
        if (response.IsSuccessStatusCode) container = (await DataWithoutDisposalAsync(response)).GetProperty("id").GetString()!;
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var listed = await DataAsync(await client.GetAsync("/api/containers"));
            container = listed.EnumerateArray().Single(item => item.GetProperty("kind").GetString() == "pool").GetProperty("id").GetString()!;
        }
        return await PipelineAsync(client, string.Empty, container);
    }

    private static async Task<Lane> PipelineAsync(HttpClient client, string tenantId, string containerId)
    {
        var host = "access-" + Unique() + ".example";
        var source = await DataAsync(await PostAsync(client, "/api/sources", new
        {
            containerId, family = "site", handle = host, coverage = "whole", completeness = "complete", posture = "ownFetch",
        }));
        var sourceId = source.GetProperty("id").GetString()!;
        var pipeline = await DataAsync(await PostAsync(client, "/api/pipelines", new { containerId, sourceId }));
        return new(tenantId, containerId, sourceId, pipeline.GetProperty("id").GetString()!, host);
    }

    private static async Task<string> KeyAsync(HttpClient client, string tenant, params string[] scopes)
    {
        var issued = await DataAsync(await PostAsync(client, $"/api/tenants/{tenant}/api-keys", new { name = "access-fixture", scopes }));
        return issued.GetProperty("secret").GetString()!;
    }

    private static async Task<string> CaptureAsync(HttpClient client, Lane lane, string title)
    {
        var url = "https://" + lane.Host + "/event";
        var result = await DataAsync(await PostAsync(client, "/api/posts/captures", new
        {
            containerId = lane.ContainerId, sourceId = lane.SourceId,
            capture = new
            {
                externalKey = url, url, requestUrl = url, finalUrl = url, httpStatus = 200, capturedAtUtc = DateTimeOffset.UtcNow,
                body = Convert.ToBase64String(Encoding.UTF8.GetBytes(title)), contentProjection = title, contentType = "text/plain",
            },
            envelope = new
            {
                schema = "wow2.post.v1", kind = "event", title, text = title,
                extraction = new { normVersion = "norm@1", rulesVersion = "rules@1", tiersRun = new[] { "deterministic", "rules" } },
            },
        }));
        return result.GetProperty("post").GetProperty("id").GetString()!;
    }

    private static object Subscription(Lane lane) => new
    { tenantId = lane.TenantId, containerId = lane.ContainerId, pipelineId = lane.PipelineId, backfill = "none" };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        await client.PostAsync(path, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));

    private static async Task ExpectAsync(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response) return await DataWithoutDisposalAsync(response);
    }

    private static async Task<JsonElement> DataWithoutDisposalAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("data").Clone();
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];
    private sealed record Lane(string TenantId, string ContainerId, string SourceId, string PipelineId, string Host);
}
