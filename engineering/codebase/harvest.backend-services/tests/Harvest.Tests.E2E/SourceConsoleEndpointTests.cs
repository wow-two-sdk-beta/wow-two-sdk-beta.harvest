using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Harvest.Tests.E2E;

/// <summary>The console's Sources routes on the wire: the overview's JSON shape, groups, names, reading pace, page
/// limits, a source's posts with their first image and a post's revisions — and the writes refused to anyone but the operator.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class SourceConsoleEndpointTests(HarvestApiFixture fixture)
{
    [Theory]
    [InlineData("GET", "/api/sources/overview?containerId=00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/sources/groups?containerId=00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/sources/groups")]
    [InlineData("PUT", "/api/sources/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/sources/00000000-0000-0000-0000-000000000001/reading")]
    [InlineData("GET", "/api/sources/00000000-0000-0000-0000-000000000001/posts")]
    [InlineData("GET", "/api/posts/00000000-0000-0000-0000-000000000001/revisions")]
    public async Task SourceRoutes_ShouldRequireAnAuthenticatedCaller(string method, string path)
    {
        using var client = fixture.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = Json(new { }) };

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Operator_ShouldGroupNamePaceAndReadASource_InTheContractShape()
    {
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var lane = await LaneAsync(admin);

        var group = await DataAsync(await admin.PostAsync("/api/sources/groups", Json(new { containerId = lane.ContainerId, name = "Afisha", description = "Listings" })));
        var again = await DataAsync(await admin.PostAsync("/api/sources/groups", Json(new { containerId = lane.ContainerId, name = "AFISHA" })));
        var groupId = group.GetProperty("id").GetString();
        again.GetProperty("id").GetString().Should().Be(groupId);
        Keys(group).Should().BeEquivalentTo("id", "containerId", "name", "description");
        (await DataAsync(await admin.GetAsync($"/api/sources/groups?containerId={lane.ContainerId}"))).EnumerateArray()
            .Select(value => value.GetProperty("id").GetString()).Should().Equal(groupId);

        var named = await DataAsync(await admin.PutAsJsonAsync($"/api/sources/{lane.SourceId}", new { name = "Afisha.uz", groupId }));
        named.GetProperty("name").GetString().Should().Be("Afisha.uz");
        named.GetProperty("groupId").GetString().Should().Be(groupId);
        var health = await DataAsync(await admin.PutAsJsonAsync($"/api/sources/{lane.SourceId}/reading", new { minDelayMs = 5000 }));
        health.GetProperty("hostKey").GetString().Should().Be(lane.Host);
        health.GetProperty("minDelayMs").GetInt32().Should().Be(5000);
        await ExpectAsync(admin.PutAsJsonAsync($"/api/sources/{lane.SourceId}/reading", new { minDelayMs = 999 }), HttpStatusCode.BadRequest);
        var limited = await DataAsync(await admin.PutAsJsonAsync($"/api/pipelines/{lane.PipelineId}", Edit(lane, new { maxPages = 7 })));
        limited.GetProperty("limits").GetProperty("maxPages").GetInt32().Should().Be(7);
        await ExpectAsync(admin.PutAsJsonAsync($"/api/pipelines/{lane.PipelineId}", Edit(lane, new { maxPages = 51 })), HttpStatusCode.BadRequest);

        var overview = (await DataAsync(await admin.GetAsync($"/api/sources/overview?containerId={lane.ContainerId}"))).EnumerateArray().Single();
        Keys(overview).Should().BeEquivalentTo("sourceId", "name", "family", "handle", "groupId", "state", "newPostsDaily", "sections", "health");
        overview.GetProperty("name").GetString().Should().Be("Afisha.uz");
        overview.GetProperty("family").GetString().Should().Be("site");
        overview.GetProperty("state").GetString().Should().Be("healthy");
        overview.GetProperty("newPostsDaily").EnumerateArray().Select(day => day.GetInt32()).Should().HaveCount(14).And.OnlyContain(day => day == 0);
        var section = overview.GetProperty("sections").EnumerateArray().Single();
        Keys(section).Should().BeEquivalentTo("pipelineId", "name", "url", "harvesting", "paused", "captureEvery", "checkEvery", "maxPages",
            "posts", "subscribers");
        section.GetProperty("captureEvery").GetString().Should().Be("PT6H");
        section.GetProperty("checkEvery").GetString().Should().Be("P1D");
        section.GetProperty("maxPages").GetInt32().Should().Be(7);
        Keys(overview.GetProperty("health")).Should().BeEquivalentTo("hostKey", "minDelayMs", "consecutiveFailures");

        var post = await CaptureAsync(admin, lane, "Concert");
        var posts = (await DataAsync(await admin.GetAsync($"/api/sources/{lane.SourceId}/posts?limit=10"))).EnumerateArray().Single();
        Keys(posts).Should().BeEquivalentTo("postId", "title", "kind", "state", "revision", "firstSeenAt", "lastChangedAt", "url");
        posts.GetProperty("kind").GetString().Should().Be("event");
        await ExpectAsync(admin.GetAsync($"/api/sources/{lane.SourceId}/posts?limit=0"), HttpStatusCode.BadRequest);
        var revision = (await DataAsync(await admin.GetAsync($"/api/posts/{post}/revisions"))).EnumerateArray().Single();
        Keys(revision).Should().BeEquivalentTo("revision", "at", "changed");
        revision.GetProperty("changed").EnumerateArray().Select(name => name.GetString()).Should().Equal("created");
        await ExpectAsync(admin.GetAsync($"/api/posts/{Guid.NewGuid()}/revisions"), HttpStatusCode.NotFound);

        var poster = $"https://{lane.Host}/poster.jpg";
        var pictured = await CaptureAsync(admin, lane, "Exhibition", [new { kind = "video", url = $"https://{lane.Host}/clip.mp4" },
            new { kind = "image", url = poster, fileRef = "files://stored/poster" }]);
        (await DataAsync(await admin.GetAsync($"/api/sources/{lane.SourceId}/posts"))).EnumerateArray()
            .Single(value => value.GetProperty("postId").GetString() == pictured).GetProperty("imageUrl").GetString().Should().Be(poster);

        var cleared = await DataAsync(await admin.PutAsJsonAsync($"/api/sources/{lane.SourceId}", new { clearGroup = true }));
        cleared.TryGetProperty("groupId", out _).Should().BeFalse();
        cleared.GetProperty("name").GetString().Should().Be("Afisha.uz");
    }

    [Fact]
    public async Task TenantKey_ShouldBeRefusedTheWrites_AndReadOnlyItsOwnSources()
    {
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await LaneAsync(admin);
        var other = await LaneAsync(admin);
        var otherPost = await CaptureAsync(admin, other, "Foreign");
        var ownPost = await CaptureAsync(admin, own, "Own");
        using var client = fixture.CreateClient(await KeyAsync(admin, own.TenantId, "pipelines:read", "pipelines:manage", "posts:read"));
        using var narrow = fixture.CreateClient(await KeyAsync(admin, own.TenantId, "events:read"));

        await ExpectAsync(client.PostAsync("/api/sources/groups", Json(new { containerId = own.ContainerId, name = "Mine" })), HttpStatusCode.Forbidden);
        await ExpectAsync(client.PutAsJsonAsync($"/api/sources/{own.SourceId}", new { name = "Mine" }), HttpStatusCode.Forbidden);
        await ExpectAsync(client.PutAsJsonAsync($"/api/sources/{own.SourceId}/reading", new { minDelayMs = 1000 }), HttpStatusCode.Forbidden);
        await ExpectAsync(client.PutAsJsonAsync($"/api/pipelines/{own.PipelineId}", Edit(own, new { maxPages = 2 })), HttpStatusCode.Forbidden);
        await ExpectAsync(client.PutAsJsonAsync($"/api/pipelines/{own.PipelineId}", Edit(own, null)), HttpStatusCode.OK);

        await ExpectAsync(client.GetAsync($"/api/sources/overview?containerId={own.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/sources/groups?containerId={own.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/sources/{own.SourceId}/posts"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/posts/{ownPost}/revisions"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/posts/{ownPost}/revisions?containerId={own.ContainerId}"), HttpStatusCode.OK);
        await ExpectAsync(client.GetAsync($"/api/sources/overview?containerId={other.ContainerId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/sources/groups?containerId={other.ContainerId}"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/sources/{other.SourceId}/posts"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/posts/{otherPost}/revisions"), HttpStatusCode.NotFound);
        await ExpectAsync(client.GetAsync($"/api/posts/{otherPost}/revisions?containerId={other.ContainerId}"), HttpStatusCode.NotFound);
        await ExpectAsync(narrow.GetAsync($"/api/sources/overview?containerId={own.ContainerId}"), HttpStatusCode.Forbidden);
        await ExpectAsync(narrow.GetAsync($"/api/sources/{own.SourceId}/posts"), HttpStatusCode.Forbidden);
    }

    private static object Edit(Lane lane, object? limits) => new
    {
        name = "Fashion", scope = new { url = $"https://{lane.Host}/ru/fashion" }, limits,
        schedules = new object[]
        {
            new { kind = "capture", mode = "interval", every = "PT6H" },
            new { kind = "check", mode = "interval", every = "P1D" },
        },
    };

    private static async Task<Lane> LaneAsync(HttpClient client)
    {
        var tenant = await DataAsync(await client.PostAsync("/api/tenants", Json(new { slug = "sources-" + Unique(), name = "Sources" })));
        var tenantId = tenant.GetProperty("id").GetString()!;
        var container = await DataAsync(await client.PostAsync("/api/containers", Json(new { kind = "private", tenantId })));
        var containerId = container.GetProperty("id").GetString()!;
        var host = "www." + Unique() + ".example";
        var source = await DataAsync(await client.PostAsync("/api/sources", Json(new
        {
            containerId, family = "site", handle = host, coverage = "whole", completeness = "complete", posture = "ownFetch",
        })));
        var sourceId = source.GetProperty("id").GetString()!;
        var pipeline = await DataAsync(await client.PostAsync("/api/pipelines", Json(new
        {
            containerId, sourceId, name = "Fashion", scope = new { url = $"https://{host}/ru/fashion" }, limits = new { maxPages = 5 },
            schedules = new object[]
            {
                new { kind = "capture", mode = "interval", every = "PT6H" },
                new { kind = "check", mode = "interval", every = "P1D" },
            },
        })));
        return new Lane(tenantId, containerId, sourceId, pipeline.GetProperty("id").GetString()!, host);
    }

    private static async Task<string> KeyAsync(HttpClient client, string tenant, params string[] scopes)
    {
        var issued = await DataAsync(await client.PostAsync($"/api/tenants/{tenant}/api-keys", Json(new { name = "sources", scopes })));
        return issued.GetProperty("secret").GetString()!;
    }

    private static async Task<string> CaptureAsync(HttpClient client, Lane lane, string title, object[]? media = null)
    {
        var url = $"https://{lane.Host}/{Unique()}";
        var result = await DataAsync(await client.PostAsync("/api/posts/captures", Json(new
        {
            containerId = lane.ContainerId, sourceId = lane.SourceId,
            capture = new
            {
                externalKey = url, url, requestUrl = url, finalUrl = url, httpStatus = 200, capturedAtUtc = DateTimeOffset.UtcNow,
                body = Convert.ToBase64String(Encoding.UTF8.GetBytes(title)), contentProjection = title, contentType = "text/plain",
            },
            envelope = new
            {
                schema = "wow2.post.v1", kind = "event", title, text = title, media = media ?? [],
                extraction = new { normVersion = "norm@1", rulesVersion = "rules@1" },
            },
        })));
        return result.GetProperty("post").GetProperty("id").GetString()!;
    }

    private static IEnumerable<string> Keys(JsonElement value) => value.EnumerateObject().Select(property => property.Name);

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task ExpectAsync(Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(content);
            using var json = JsonDocument.Parse(content);
            return json.RootElement.GetProperty("data").Clone();
        }
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private sealed record Lane(string TenantId, string ContainerId, string SourceId, string PipelineId, string Host);
}
