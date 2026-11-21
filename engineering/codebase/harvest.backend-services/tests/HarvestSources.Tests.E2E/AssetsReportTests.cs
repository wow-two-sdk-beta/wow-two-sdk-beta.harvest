using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HarvestSources.Api.Media;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace HarvestSources.Tests.E2E;

/// <summary>The copier records each attempt's outcome and a copy's size, and <c>assets report</c> reads them as counts only.</summary>
public sealed partial class SourceAssetsTests
{
    private const string SellerPhone = "+998901234567";
    private const string SellerEmail = "seller@example.test";
    private const string SellerHandle = "@seller_example";

    [Fact]
    public async Task A_successful_copy_records_copied_the_bytes_uploaded_and_when_it_finished()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lane = await fixture.SourceAsync("site", "web.example.test", "ownFetch");
        await fixture.CaptureAsync(lane, "https://web.example.test/post.png");
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await RecordAsync(fixture, "kind='image'")).Should().Be("-|-|false|false", "a queued copy has no attempt on record");

        var asset = (await store.ClaimAsync(default))!;
        fixture.Fetcher.Body = new byte[2345];
        await fixture.Worker().CopyAsync(asset, default);

        fixture.Files.Uploads.Should().ContainSingle();
        (await RecordAsync(fixture, "kind='image'")).Should().Be("copied|2345|true|true", "the bytes uploaded are the fetched body's length");
    }

    [Fact]
    public async Task A_failed_attempt_records_why_it_did_not_copy_and_keeps_the_wait_it_always_had()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lane = await fixture.SourceAsync("site", "web.example.test", "ownFetch");

        // outcome|bytes|copied|minutes until the next attempt
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://other.example.test/a.png"))).Should().Be("rejected|-|false|10080");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/gone.png"), fetcher => fetcher.Outcome = FetchOutcome.Gone))
            .Should().Be("fetch_gone|-|false|1440");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/robots.png"), fetcher => fetcher.Outcome = FetchOutcome.RobotsDisallowed))
            .Should().Be("fetch_robots_disallowed|-|false|1440");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/limited.png"), fetcher => fetcher.Outcome = FetchOutcome.RateLimited))
            .Should().Be("fetch_rate_limited|-|false|60");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/moved.png"), fetcher => fetcher.FinalUrl = new Uri("https://untrusted.example/image.png")))
            .Should().Be("redirected_off_policy|-|false|1440");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/page.png"), fetcher => fetcher.ContentType = "text/html"))
            .Should().Be("unsupported_type|-|false|1440");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/empty.png"), fetcher => fetcher.Body = []))
            .Should().Be("empty|-|false|1440");
        (await AttemptAsync(fixture, await JobAsync(fixture, lane, "https://web.example.test/big.png"), fetcher => fetcher.Body = new byte[5 * 1024 * 1024 + 1]))
            .Should().Be("too_large|-|false|1440");
        fixture.Files.Uploads.Should().BeEmpty("no refused attempt reached Files");

        var declined = await JobAsync(fixture, lane, "https://web.example.test/declined.png");
        (await AttemptAsync(fixture, declined, declines: true)).Should().Be("files_declined|-|false|15", "the Files failure keeps its 15-minute retry");
        (await AttemptAsync(fixture, declined)).Should().Be($"copied|{FixtureFetcher.Png.Length}|true|0", "a later success replaces the failure on record");
        fixture.Files.Uploads.Should().ContainSingle();
    }

    [Fact]
    public async Task Assets_report_counts_copies_by_state_outcome_and_bytes_and_live_posts_by_contact_and_prints_no_row_data()
    {
        await using var fixture = await Fixture.CreateAsync();
        var shop = await fixture.SourceAsync("site", "shop.example.test", "ownFetch");
        var other = await DataAsync(await fixture.Client.PostAsJsonAsync("/api/sources", new
        {
            containerId = shop.Container, family = "site", handle = "other.example.test", posture = "ownFetch", coverage = "filtered", completeness = "window",
        }));
        var otherLane = shop with { Source = other.GetProperty("id").GetString()! };
        var quiet = await fixture.SourceAsync("site", "quiet.example.test", "ownFetch");
        var empty = await fixture.SourceAsync("site", "empty.example.test", "ownFetch");
        var paused = await fixture.SourceAsync("site", "paused.example.test", "ownFetch");
        // every value a row holds that the report must not print: contacts, origin URLs, file ids (with and without hyphens), post URLs
        var leaks = new List<string> { SellerPhone, SellerPhone[1..], SellerEmail, SellerHandle, "https://", "instagram" };

        // shop: five live posts and the one it shares with other; a gone, a merged and a retired-link post are not live
        var first = await PostAsync(fixture, shop, ("phone", SellerPhone), ("email", SellerEmail));
        await PostAsync(fixture, shop, ("phone", SellerPhone));
        await PostAsync(fixture, shop, ("email", SellerEmail));
        await PostAsync(fixture, shop);
        await PostAsync(fixture, shop, ("handle", SellerHandle));
        var shared = await PostAsync(fixture, shop, ("phone", SellerPhone));
        var gone = await PostAsync(fixture, shop, ("phone", SellerPhone));
        var merged = await PostAsync(fixture, shop, ("phone", SellerPhone));
        var retired = await PostAsync(fixture, shop, ("phone", SellerPhone));
        await fixture.ExecuteAsync($"UPDATE harvest_posts SET state='gone' WHERE id='{gone}'");
        await fixture.ExecuteAsync($"UPDATE harvest_posts SET merged_into='{first}' WHERE id='{merged}'");
        await fixture.ExecuteAsync($"UPDATE harvest_post_links SET retired_at=now(),retired_reason='moved' WHERE post_id='{retired}'");
        await fixture.ExecuteAsync($"""
            INSERT INTO harvest_post_links(id,container_id,source_id,post_id,external_key,url,url_key,origin_key,role,state_since,first_seen_at,last_changed_at,check_capability)
            SELECT gen_random_uuid(),container_id,'{otherLane.Source}',post_id,'second-link',url || '/second',url_key,origin_key,role,state_since,first_seen_at,last_changed_at,check_capability
            FROM harvest_post_links WHERE source_id='{shop.Source}' AND post_id='{shared}'
            """);
        await PostAsync(fixture, otherLane, ("phone", SellerPhone));
        await PostAsync(fixture, otherLane);
        await PostAsync(fixture, quiet);
        (await fixture.ScalarAsync<int>($"SELECT jsonb_array_length(base->'contacts') FROM harvest_posts WHERE id='{first}'"))
            .Should().Be(2, "the report reads the contacts the resolved post carries");

        // shop: seven image jobs, four copied (three sized, one from before the release), two due (one failed, one never tried), one deferred
        leaks.Add(await SeedCopyAsync(fixture, shop, "image", "https://shop.example.test/img/1.png", 1000, "copied"));
        leaks.Add(await SeedCopyAsync(fixture, shop, "image", "https://shop.example.test/img/2.png", 3000, "copied"));
        leaks.Add(await SeedCopyAsync(fixture, shop, "image", "https://shop.example.test/img/3.png", 2000, "copied"));
        leaks.Add(await SeedCopyAsync(fixture, shop, "image", "https://shop.example.test/img/4.png", null, null));
        await SeedQueuedAsync(fixture, shop, "image", "https://shop.example.test/img/5.png", "fetch_gone", "now() - interval '1 hour'");
        await SeedQueuedAsync(fixture, shop, "image", "https://shop.example.test/img/6.png", "too_large", "now() + interval '1 day'");
        await SeedQueuedAsync(fixture, shop, "image", "https://shop.example.test/img/7.png", null, "now() - interval '1 minute'");
        leaks.Add(await SeedCopyAsync(fixture, shop, "favicon", "https://shop.example.test/", 777, "copied"));
        leaks.Add(await SeedCopyAsync(fixture, otherLane, "image", "https://other.example.test/img/1.png", 500, "copied"));
        await SeedQueuedAsync(fixture, otherLane, "image", "https://other.example.test/img/2.png", "files_declined", "now() - interval '1 minute'");
        await SeedQueuedAsync(fixture, otherLane, "favicon", "https://other.example.test/", "fetch_gone", "now() - interval '1 minute'");
        leaks.Add(await SeedCopyAsync(fixture, paused, "image", "https://paused.example.test/img/1.png", 100, "copied"));
        await fixture.ExecuteAsync($"UPDATE harvest_sources SET state='paused' WHERE id='{paused.Source}'");
        leaks.AddRange((await fixture.ScalarAsync<string>("SELECT string_agg(origin_url, ',') FROM harvest_source_assets")).Split(','));
        leaks.AddRange((await fixture.ScalarAsync<string>("SELECT string_agg(url, ',') FROM harvest_post_links")).Split(','));
        leaks.AddRange((await fixture.ScalarAsync<string>("SELECT string_agg(file_id::text, ',') FROM harvest_source_assets")).Split(',').Select(id => id.Replace("-", "")));
        string before = await fixture.ScalarAsync<string>("SELECT md5(string_agg(a::text, '|' ORDER BY a.id)) FROM harvest_source_assets a");

        var report = await ReportAsync(fixture, "report");

        report.Exit.Should().Be(0, report.Error);
        report.Error.Should().BeEmpty();
        report.Output.TrimEnd().Split('\n').Should().ContainSingle("the report is one JSON line");
        foreach (string leak in leaks)
            report.Output.Should().NotContain(leak, "the report prints counts only");
        using var document = JsonDocument.Parse(report.Output);
        var root = document.RootElement;
        DateTimeOffset.Parse(root.GetProperty("generatedAt").GetString()!).Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        root.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("handle").GetString())
            .Should().Equal("other.example.test", "quiet.example.test", "shop.example.test");
        root.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("family").GetString()).Should().OnlyContain(family => family == "site");

        var shopReport = SourceOf(root, shop.Source);
        shopReport.GetProperty("handle").GetString().Should().Be("shop.example.test");
        Same(shopReport.GetProperty("images"), """{"jobs":7,"copied":4,"due":2,"deferred":1,"byOutcome":{"copied":3,"fetch_gone":1,"too_large":1,"unrecorded":2}}""");
        Same(shopReport.GetProperty("bytes"), """{"sized":3,"unsized":1,"total":6000,"min":1000,"median":2000,"max":3000}""");
        Same(shopReport.GetProperty("favicon"), """{"copied":true}""");
        Same(shopReport.GetProperty("posts"), """{"total":6,"withContact":5,"withPhone":3,"withEmail":2}""");
        var otherReport = SourceOf(root, otherLane.Source);
        Same(otherReport.GetProperty("images"), """{"jobs":2,"copied":1,"due":1,"deferred":0,"byOutcome":{"copied":1,"files_declined":1}}""");
        Same(otherReport.GetProperty("bytes"), """{"sized":1,"unsized":0,"total":500,"min":500,"median":500,"max":500}""");
        Same(otherReport.GetProperty("favicon"), """{"copied":false}""");
        Same(otherReport.GetProperty("posts"), """{"total":3,"withContact":2,"withPhone":2,"withEmail":0}""");
        var quietReport = SourceOf(root, quiet.Source);
        Same(quietReport.GetProperty("images"), """{"jobs":0,"copied":0,"due":0,"deferred":0,"byOutcome":{}}""");
        Same(quietReport.GetProperty("bytes"), """{"sized":0,"unsized":0,"total":null,"min":null,"median":null,"max":null}""");
        Same(quietReport.GetProperty("favicon"), """{"copied":null}""");
        Same(quietReport.GetProperty("posts"), """{"total":1,"withContact":0,"withPhone":0,"withEmail":0}""");
        root.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("sourceId").GetString())
            .Should().NotContain(new[] { empty.Source, paused.Source }, "a source with no job and no post, and a paused one, are not listed");

        var totals = root.GetProperty("totals");
        totals.GetProperty("sources").GetInt32().Should().Be(3);
        Same(totals.GetProperty("images"), """{"jobs":9,"copied":5,"due":3,"deferred":1,"byOutcome":{"copied":4,"fetch_gone":1,"files_declined":1,"too_large":1,"unrecorded":2}}""");
        Same(totals.GetProperty("bytes"), """{"sized":4,"unsized":1,"total":6500,"min":500,"median":1000,"max":3000}""");
        Same(totals.GetProperty("posts"), """{"total":9,"withContact":6,"withPhone":4,"withEmail":2}""");

        var limited = await ReportAsync(fixture, "report", "--sources", $"{otherLane.Source},{quiet.Source},{Guid.NewGuid()}");
        limited.Exit.Should().Be(0, limited.Error);
        using var limitedDocument = JsonDocument.Parse(limited.Output);
        limitedDocument.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("handle").GetString())
            .Should().Equal("other.example.test", "quiet.example.test");
        Same(limitedDocument.RootElement.GetProperty("totals"), """
            {"sources":2,"images":{"jobs":2,"copied":1,"due":1,"deferred":0,"byOutcome":{"copied":1,"files_declined":1}},
             "bytes":{"sized":1,"unsized":0,"total":500,"min":500,"median":500,"max":500},
             "posts":{"total":4,"withContact":2,"withPhone":2,"withEmail":0}}
            """);
        (await fixture.ScalarAsync<string>("SELECT md5(string_agg(a::text, '|' ORDER BY a.id)) FROM harvest_source_assets a"))
            .Should().Be(before, "the report writes nothing");
    }

    [Fact]
    public async Task Assets_report_refuses_an_unknown_sub_command_and_a_bad_argument_naming_only_the_argument()
    {
        await using var fixture = await Fixture.CreateAsync();

        var none = await ReportAsync(fixture);
        var unknown = await ReportAsync(fixture, "rebuild");
        var badIds = await ReportAsync(fixture, "report", "--sources", "not-an-id");
        var badOption = await ReportAsync(fixture, "report", "--limit", "5");
        var stray = await ReportAsync(fixture, "report", "extra");

        foreach (var usage in new[] { none, unknown, stray })
        {
            usage.Exit.Should().Be(2);
            usage.Output.Should().BeEmpty();
            usage.Error.Should().Contain("Usage: assets report [--sources <id,id>]");
        }
        badIds.Exit.Should().Be(2);
        badIds.Error.Should().Contain("--sources").And.NotContain("not-an-id");
        badOption.Exit.Should().Be(2);
        badOption.Error.Should().Contain("--limit").And.NotContain("5");
        badIds.Output.Should().BeEmpty();
        badOption.Output.Should().BeEmpty();
    }

    /// <summary>Reads a job's record as <c>outcome|bytes|copied|finished</c>, a dash for a value not recorded.</summary>
    private static Task<string> RecordAsync(Fixture fixture, string where) => fixture.ScalarAsync<string>(
        $"SELECT concat_ws('|', COALESCE(outcome,'-'), COALESCE(bytes::text,'-'), (file_id IS NOT NULL)::text, (finished_at IS NOT NULL)::text) FROM harvest_source_assets WHERE {where}");

    /// <summary>Queues one image job by hand, for an attempt the claim query would not hand out.</summary>
    private static async Task<SourceAsset> JobAsync(Fixture fixture, Lane lane, string url)
    {
        Guid id = Guid.NewGuid();
        await fixture.ExecuteAsync($"INSERT INTO harvest_source_assets(id,source_id,kind,origin_url) VALUES('{id}','{lane.Source}','image','{url}')");
        return new SourceAsset(id, Guid.Parse(lane.Source), false, url, "web.example.test", false);
    }

    /// <summary>Runs one attempt against a fetch and a Files that behave as <paramref name="configure"/> and <paramref name="declines"/> say.</summary>
    /// <returns>The job's record as <c>outcome|bytes|copied|minutes until the next attempt</c>.</returns>
    private static async Task<string> AttemptAsync(Fixture fixture, SourceAsset asset, Action<FixtureFetcher>? configure = null, bool declines = false)
    {
        fixture.Fetcher.Outcome = FetchOutcome.Ok;
        fixture.Fetcher.ContentType = "image/png";
        fixture.Fetcher.Body = FixtureFetcher.Png;
        fixture.Fetcher.FinalUrl = null;
        fixture.Files.Declines = declines;
        configure?.Invoke(fixture.Fetcher);
        await fixture.Worker().CopyAsync(asset, default);
        return await fixture.ScalarAsync<string>($"""
            SELECT concat_ws('|', outcome, COALESCE(bytes::text,'-'), (file_id IS NOT NULL)::text, abs(round(extract(epoch FROM retry_at-now())/60))::text)
            FROM harvest_source_assets WHERE id='{asset.Id}'
            """);
    }

    /// <summary>Seeds one job as a finished copy leaves it: a file, and the bytes and outcome the copier records (none, for a copy made
    /// before the release).</summary>
    /// <returns>The file id given to the copy, so the caller can check it is not printed.</returns>
    private static async Task<string> SeedCopyAsync(Fixture fixture, Lane lane, string kind, string url, long? bytes, string? outcome)
    {
        Guid file = Guid.NewGuid();
        string size = bytes?.ToString() ?? "NULL", recorded = outcome is null ? "NULL" : $"'{outcome}'", finished = outcome is null ? "NULL" : "now()";
        await fixture.ExecuteAsync($"""
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url,file_id,bytes,outcome,finished_at)
            VALUES(gen_random_uuid(),'{lane.Source}','{kind}','{url}','{file}',{size},{recorded},{finished})
            """);
        return file.ToString("D");
    }

    /// <summary>Seeds one job that is not copied: its last outcome, if it was tried, and when it retries.</summary>
    private static async Task SeedQueuedAsync(Fixture fixture, Lane lane, string kind, string url, string? outcome, string retry)
    {
        string recorded = outcome is null ? "NULL" : $"'{outcome}'", finished = outcome is null ? "NULL" : "now()";
        await fixture.ExecuteAsync($"""
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url,retry_at,outcome,finished_at)
            VALUES(gen_random_uuid(),'{lane.Source}','{kind}','{url}',{retry},{recorded},{finished})
            """);
    }

    /// <summary>Captures one post with the given contacts, as <c>(kind, value)</c> pairs, and returns its id.</summary>
    private static async Task<Guid> PostAsync(Fixture fixture, Lane lane, params (string Kind, string Value)[] contacts)
    {
        string url = "https://www.instagram.com/p/" + Guid.NewGuid().ToString("N") + "/";
        await DataAsync(await fixture.Client.PostAsJsonAsync("/api/posts/captures", new
        {
            containerId = lane.Container, sourceId = lane.Source,
            capture = new { externalKey = url, url, requestUrl = url, finalUrl = url, httpStatus = 200, capturedAtUtc = DateTimeOffset.UtcNow,
                body = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(url)), contentProjection = url, contentType = "text/plain" },
            envelope = new { schema = "wow2.post.v1", kind = "other", title = url, text = url,
                contacts = contacts.Select(contact => new { kind = contact.Kind, value = contact.Value, raw = contact.Value }).ToArray(),
                extraction = new { normVersion = "norm@1", rulesVersion = "rules@1" } },
        }));
        return Guid.Parse(await fixture.ScalarAsync<string>($"SELECT post_id::text FROM harvest_post_links WHERE source_id='{lane.Source}' AND external_key='{url}'"));
    }

    private static async Task<(int Exit, string Output, string Error)> ReportAsync(Fixture fixture, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await AssetsVerb.RunAsync(fixture.Host.Services, arguments, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    private static JsonElement SourceOf(JsonElement root, string sourceId) =>
        root.GetProperty("sources").EnumerateArray().Single(source => source.GetProperty("sourceId").GetString() == sourceId);

    private static void Same(JsonElement actual, string expected)
    {
        using var wanted = JsonDocument.Parse(expected);
        JsonElement.DeepEquals(actual, wanted.RootElement).Should().BeTrue($"expected {wanted.RootElement.GetRawText()} but read {actual.GetRawText()}");
    }
}
