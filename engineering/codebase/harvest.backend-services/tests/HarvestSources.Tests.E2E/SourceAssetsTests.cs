using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Files.Store;
using WoW.Two.Sdk.Beta.Be.Files.Uploads;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace HarvestSources.Tests.E2E;

public sealed partial class SourceAssetsTests
{
    private const string CdnImage = "https://scontent.example.cdninstagram.com/post.jpg?signature=fixture";
    private const string SecondImage = "https://scontent.example.fbcdn.net/post.jpg?signature=fixture";

    [Fact]
    public async Task Existing_Instagram_posts_are_copied_privately_and_served_only_through_their_source()
    {
        await using var fixture = await Fixture.CreateAsync();
        var lane = await fixture.SourceAsync("instagram", "publisher", "official");
        await fixture.CaptureAsync(lane, CdnImage, SecondImage, "https://cdninstagram.com.evil.test/post.jpg");
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(2);

        var worker = fixture.Worker();
        var asset = (await store.ClaimAsync(default))!;
        asset.Instagram.Should().BeTrue();
        asset.Favicon.Should().BeFalse();
        await worker.CopyAsync(asset, default);
        await worker.CopyAsync((await store.ClaimAsync(default))!, default);
        (await store.ClaimAsync(default)).Should().BeNull();
        fixture.Files.Uploads.Should().HaveCount(2).And.OnlyContain(upload => upload.Visibility == FileVisibility.Private);
        fixture.Files.Uploads.Select(upload => upload.IdempotencyKey).Should().OnlyHaveUniqueItems();
        fixture.Fetcher.Requests.Should().HaveCount(2).And.OnlyContain(request => !request.CacheResponse && request.Cookies == null && request.RobotsPolicy == RobotsPolicy.Ignore);
        foreach (var request in fixture.Fetcher.Requests)
        {
            request.DestinationPolicy!.Allows(new Uri(SecondImage)).Should().BeTrue();
            request.DestinationPolicy.Allows(new Uri("https://127.0.0.1/private")).Should().BeFalse();
            request.DestinationPolicy.Allows(new Uri("https://untrusted.example/image.jpg")).Should().BeFalse();
        }

        JsonElement posts = await DataAsync(await fixture.Client.GetAsync($"/api/sources/{lane.Source}/posts"));
        string[] images = posts.EnumerateArray().Single().GetProperty("imageUrls").EnumerateArray().Select(value => value.GetString()!).ToArray();
        images.Take(2).Should().OnlyContain(url => url.StartsWith($"/api/sources/{lane.Source}/assets/", StringComparison.Ordinal));
        images[2].Should().Be("https://cdninstagram.com.evil.test/post.jpg");
        using var image = await fixture.Client.GetAsync(images[0]);
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        (await image.Content.ReadAsByteArrayAsync()).Should().Equal(FixtureFetcher.Png);
        image.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        image.Headers.CacheControl!.Private.Should().BeTrue();
        using var anonymous = fixture.Host.GetTestClient();
        (await anonymous.GetAsync(images[0])).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var narrow = await fixture.ReaderAsync(lane.Tenant, "pipelines:read");
        (await narrow.GetAsync(images[0])).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var other = await fixture.SourceAsync("instagram", "otherpublisher", "official");
        using var foreign = await fixture.ReaderAsync(other.Tenant, "posts:read");
        (await foreign.GetAsync(images[0])).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.Client.GetAsync(images[0].Replace(lane.Source, other.Source, StringComparison.Ordinal))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await store.QueueMissingAsync(default);
        (await store.ClaimAsync(default)).Should().BeNull();
    }

    [Theory]
    [InlineData(15, 15)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    public async Task Full_carousels_are_copied_with_a_bounded_image_list_and_web_images_still_obey_robots(int count, int expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        var instagram = await fixture.SourceAsync("instagram", "carousel", "official");
        var web = await fixture.SourceAsync("site", "web.example.test", "ownFetch");
        string[] originals = Enumerable.Range(1, count).Select(index => $"https://scontent.example.cdninstagram.com/image-{index}.jpg?signature=fixture").ToArray();
        await fixture.CaptureAsync(instagram, originals);
        await fixture.CaptureAsync(web, "https://web.example.test/post.png");
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(expected + 1);
        using var worker = fixture.Worker();
        while (await store.ClaimAsync(default) is { } asset) await worker.CopyAsync(asset, default);
        fixture.Files.Uploads.Should().HaveCount(expected + 1);
        fixture.Fetcher.Requests.Where(request => request.Uri.Host.EndsWith(".cdninstagram.com", StringComparison.Ordinal))
            .Should().HaveCount(expected).And.OnlyContain(request => request.RobotsPolicy == RobotsPolicy.Ignore);
        fixture.Fetcher.Requests.Single(request => request.Uri.Host == "web.example.test").RobotsPolicy.Should().Be(RobotsPolicy.Obey);
        foreach (var request in fixture.Fetcher.Requests)
        {
            request.DestinationPolicy!.Allows(new Uri("https://www.instagram.com/document")).Should().BeFalse();
            request.DestinationPolicy.Allows(new Uri("https://scontent.example.cdninstagram.com.evil.test/image.jpg")).Should().BeFalse();
        }
        JsonElement posts = await DataAsync(await fixture.Client.GetAsync($"/api/sources/{instagram.Source}/posts"));
        string[] images = posts.EnumerateArray().Single().GetProperty("imageUrls").EnumerateArray().Select(value => value.GetString()!).ToArray();
        images.Should().HaveCount(expected).And.OnlyContain(url => url.StartsWith($"/api/sources/{instagram.Source}/assets/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Instagram_copies_respect_shared_host_backoff_source_switches_and_existing_web_rules()
    {
        await using var fixture = await Fixture.CreateAsync();
        var instagram = await fixture.SourceAsync("instagram", "publisher", "official");
        var web = await fixture.SourceAsync("site", "web.example.test", "ownFetch");
        var unofficial = await fixture.SourceAsync("instagram", "unofficial", "ownFetch");
        await fixture.CaptureAsync(instagram, CdnImage);
        await fixture.CaptureAsync(web, SecondImage, "https://web.example.test/post.png");
        await fixture.CaptureAsync(unofficial, SecondImage);
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(2);
        await fixture.ExecuteAsync("UPDATE harvest_sources SET state='paused' WHERE id=@source", ("source", Guid.Parse(web.Source)));
        await fixture.ExecuteAsync("INSERT INTO harvest_host_state(host_key,paused_until) VALUES ('www.instagram.com',now()+interval '1 hour') ON CONFLICT(host_key) DO UPDATE SET paused_until=excluded.paused_until");
        (await store.ClaimAsync(default)).Should().BeNull();
        await fixture.ExecuteAsync("UPDATE harvest_host_state SET paused_until=NULL,retry_after_until=now()+interval '1 hour' WHERE host_key='www.instagram.com'");
        (await store.ClaimAsync(default)).Should().BeNull();
        await fixture.ExecuteAsync("UPDATE harvest_host_state SET retry_after_until=NULL WHERE host_key='www.instagram.com'");
        await fixture.ExecuteAsync("UPDATE harvest_sources SET permission='denied' WHERE id=@source", ("source", Guid.Parse(instagram.Source)));
        (await store.ClaimAsync(default)).Should().BeNull();
        await fixture.ExecuteAsync("UPDATE harvest_sources SET permission='granted' WHERE id=@source", ("source", Guid.Parse(instagram.Source)));
        // A recapture can rotate the signed CDN URL without changing the post. Stop retrying its superseded URL.
        await fixture.ExecuteAsync("UPDATE harvest_posts SET base=jsonb_set(base,'{media}',jsonb_build_array(jsonb_build_object('kind','image','url',@fresh))) WHERE container_id=@container",
            ("fresh", SecondImage), ("container", Guid.Parse(instagram.Container)));
        (await store.ClaimAsync(default)).Should().BeNull();
        await store.QueueMissingAsync(default);
        var asset = (await store.ClaimAsync(default))!;
        asset.Url.Should().Be(SecondImage);
        fixture.Fetcher.ContentType = "text/html";
        await fixture.Worker().CopyAsync(asset, default);
        fixture.Files.Uploads.Should().BeEmpty();
        (await store.FindAsync(Guid.Parse(instagram.Source), asset.Id, default)).Should().BeNull();
        fixture.Fetcher.ContentType = "image/png";
        fixture.Fetcher.Body = new byte[5 * 1024 * 1024 + 1];
        await fixture.Worker().CopyAsync(asset, default);
        fixture.Files.Uploads.Should().BeEmpty();
        fixture.Fetcher.Body = FixtureFetcher.Png;
        fixture.Fetcher.FinalUrl = new Uri("https://untrusted.example/image.png");
        await fixture.Worker().CopyAsync(asset, default);
        fixture.Files.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task Image_copies_of_a_post_whose_every_link_is_an_off_source_stay_queued_and_unclaimed_until_it_is_turned_on()
    {
        await using var fixture = await Fixture.CreateAsync();
        var off = await fixture.SourceAsync("instagram", "offpublisher", "official");
        var live = await fixture.SourceAsync("instagram", "livepublisher", "official");
        await fixture.CaptureAsync(off, CdnImage);
        await fixture.CaptureAsync(live, SecondImage);
        await fixture.TurnOffAsync(off);
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(2, "the copy is queued for the off source too");

        var worker = fixture.Worker();
        var asset = (await store.ClaimAsync(default))!;
        asset.Url.Should().Be(SecondImage);
        await worker.CopyAsync(asset, default);

        (await store.ClaimAsync(default)).Should().BeNull("the off source's copy stays unclaimed");
        fixture.Fetcher.Requests.Should().ContainSingle().Which.Uri.ToString().Should().Be(SecondImage, "nothing was fetched for the off source");
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets WHERE file_id IS NULL")).Should().Be(1, "its row stays queued");

        await fixture.ExecuteAsync("UPDATE harvest_catalog_sources SET turned_on=true WHERE key=@key", ("key", Fixture.OffKey));
        var resumed = (await store.ClaimAsync(default))!;
        resumed.Url.Should().Be(CdnImage, "turning the source on resumes the queued copy as it was");
        await worker.CopyAsync(resumed, default);
        fixture.Files.Uploads.Should().HaveCount(2);
    }

    [Fact]
    public async Task Image_copies_still_fetch_when_another_source_that_is_on_also_links_the_post()
    {
        await using var fixture = await Fixture.CreateAsync();
        var off = await fixture.SourceAsync("instagram", "offpublisher", "official");
        await fixture.CaptureAsync(off, CdnImage);
        var other = await DataAsync(await fixture.Client.PostAsJsonAsync("/api/sources", new
        {
            containerId = off.Container, family = "instagram", handle = "onpublisher", posture = "official", coverage = "filtered", completeness = "window",
        }));
        await fixture.ExecuteAsync("""
            INSERT INTO harvest_post_links(id,container_id,source_id,post_id,external_key,url,url_key,origin_key,role,state_since,first_seen_at,last_changed_at,check_capability)
            SELECT gen_random_uuid(),container_id,@other,post_id,'second-link',url || '/second',url_key,origin_key,role,state_since,first_seen_at,last_changed_at,check_capability
            FROM harvest_post_links WHERE source_id=@off
            """, ("other", Guid.Parse(other.GetProperty("id").GetString()!)), ("off", Guid.Parse(off.Source)));
        await fixture.TurnOffAsync(off);
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(2);

        (await store.ClaimAsync(default)).Should().NotBeNull("the post is not held by off sources alone");
        (await store.ClaimAsync(default)).Should().NotBeNull("the off source's own copy of the same post fetches as well");
        (await store.ClaimAsync(default)).Should().BeNull();
    }

    [Theory]
    [InlineData("https://scontent.example.cdninstagram.com/a.jpg", true)]
    [InlineData("https://scontent.example.fbcdn.net/a.jpg", true)]
    [InlineData("https://scontent.example.fbcdn.net:443/a.jpg", true)]
    [InlineData("http://scontent.example.fbcdn.net/a.jpg", false)]
    [InlineData("https://scontent.example.fbcdn.net:8443/a.jpg", false)]
    [InlineData("https://user:password@scontent.example.fbcdn.net/a.jpg", false)]
    [InlineData("https://scontent.example.fbcdn.net/a.jpg?access_token=fixture", false)]
    [InlineData("https://fbcdn.net/a.jpg", false)]
    [InlineData("https://fbcdn.net.evil.test/a.jpg", false)]
    [InlineData("https://127.0.0.1/a.jpg", false)]
    public void Instagram_destination_only_allows_credential_free_Meta_CDN_images(string url, bool allowed)
    {
        new SourceAssetDestination("publisher", instagram: true).Allows(new Uri(url)).Should().Be(allowed);
        new SourceAssetDestination("web.example.test", instagram: false).Allows(new Uri(url)).Should().BeFalse();
        new SourceAssetDestination("web.example.test", instagram: false).Allows(new Uri("https://web.example.test/image.jpg")).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://www.olx.uz/a.jpg", true)]
    [InlineData("https://apollo.olxcdn.com/a.jpg", true)]
    [InlineData("https://APOLLO.OLXCDN.COM/a.jpg", true)]
    [InlineData("https://olxcdn.com/a.jpg", true)]
    [InlineData("https://a.b.olxcdn.com/a.jpg", true)]
    [InlineData("https://apollo.olxcdn.com:443/a.jpg", true)]
    [InlineData("https://olxcdn.com.evil.test/a.jpg", false)]
    [InlineData("https://apollo.olxcdn.com.evil.test/a.jpg", false)]
    [InlineData("https://evilolxcdn.com/a.jpg", false)]
    [InlineData("https://apollo.evilolxcdn.com/a.jpg", false)]
    [InlineData("https://olxcdn.co/a.jpg", false)]
    [InlineData("http://apollo.olxcdn.com/a.jpg", false)]
    [InlineData("https://apollo.olxcdn.com:8443/a.jpg", false)]
    [InlineData("https://user@apollo.olxcdn.com/a.jpg", false)]
    [InlineData("https://user:password@apollo.olxcdn.com/a.jpg", false)]
    [InlineData("https://127.0.0.1/a.jpg", false)]
    [InlineData("https://scontent.example.fbcdn.net/a.jpg", false)]
    public void Web_destination_allows_the_source_host_and_the_declared_domain_family_only(string url, bool allowed)
    {
        new SourceAssetDestination("www.olx.uz", instagram: false, ["olxcdn.com"]).Allows(new Uri(url)).Should().Be(allowed);
        // Undeclared sources keep today's rule: their own host and nothing else.
        new SourceAssetDestination("www.olx.uz", instagram: false).Allows(new Uri(url)).Should().Be(new Uri(url).Host == "www.olx.uz");
        // Declared domains never widen the Instagram branch, which still allows Meta's media CDNs only.
        new SourceAssetDestination("www.olx.uz", instagram: true, ["olxcdn.com"]).Allows(new Uri(url))
            .Should().Be(url == "https://scontent.example.fbcdn.net/a.jpg");
    }

    [Fact]
    public async Task A_declared_image_domain_queues_the_first_images_only_and_copies_them_privately_at_the_declared_pace()
    {
        await using var fixture = await Fixture.CreateAsync(new MediaAdapter("shop.example.test", new SourceMediaHosts { Domains = ["cdn.example"], Limit = 2 }));
        var lane = await fixture.SourceAsync("site", "shop.example.test", "ownFetch");
        string[] original = ["https://img.cdn.example/1.jpg", "https://img.cdn.example/2.jpg", "https://img.cdn.example/3.jpg", "https://cdn.example.evil.test/4.jpg"];
        await fixture.CaptureAsync(lane, original);
        // Every image of these posts is inside the limit, so only its host, scheme, port or userinfo keeps it out.
        string[] rejected =
        [
            "https://cdn.example.evil.test/a.jpg", "https://evilcdn.example/b.jpg", "https://img.cdn.example:8443/c.jpg",
            "https://user@img.cdn.example/d.jpg", "http://img.cdn.example/e.jpg", "https://cdn.example.evil.test@img.cdn.example/f.jpg",
            "https://img.cdn.example\\@evil.test/g.jpg", "ftp://img.cdn.example/h.jpg",
        ];
        foreach (var pair in rejected.Chunk(2)) await fixture.CaptureAsync(lane, pair);
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(2);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets WHERE origin_url IN ('https://img.cdn.example/1.jpg','https://img.cdn.example/2.jpg')"))
            .Should().Be(2);

        // A recapture that moves an image past the limit stops its claim; moving it back restores it.
        string Media(params string[] urls) => JsonSerializer.Serialize(urls.Select(url => new { kind = "image", url }));
        const string Reorder = "UPDATE harvest_posts SET base=jsonb_set(base,'{media}',@media::jsonb) WHERE container_id=@container AND jsonb_array_length(base->'media')=4";
        var container = ("container", (object)Guid.Parse(lane.Container));
        await fixture.ExecuteAsync(Reorder, ("media", Media(original[1], original[2], original[3], original[0])), container);
        using var worker = fixture.Worker();
        var first = (await store.ClaimAsync(default))!;
        first.SourceId.Should().Be(Guid.Parse(lane.Source));
        first.Url.Should().Be(original[1]);
        await worker.CopyAsync(first, default);
        (await store.ClaimAsync(default)).Should().BeNull();
        await fixture.ExecuteAsync(Reorder, ("media", Media(original)), container);
        while (await store.ClaimAsync(default) is { } asset) await worker.CopyAsync(asset, default);

        fixture.Files.Uploads.Should().HaveCount(2).And.OnlyContain(upload => upload.Visibility == FileVisibility.Private);
        fixture.Files.Uploads.Select(upload => upload.IdempotencyKey).Should().OnlyHaveUniqueItems();
        fixture.Fetcher.Requests.Select(request => request.Uri.AbsoluteUri).Should().BeEquivalentTo(original[..2]);
        fixture.Fetcher.Requests.Should().OnlyContain(request => request.MinDelay == TimeSpan.FromSeconds(2)
            && request.RobotsPolicy == RobotsPolicy.Obey && !request.CacheResponse && request.Cookies == null);
        foreach (var request in fixture.Fetcher.Requests)
        {
            request.DestinationPolicy!.Allows(new Uri("https://other.cdn.example/x.jpg")).Should().BeTrue();
            request.DestinationPolicy.Allows(new Uri("https://shop.example.test/x.jpg")).Should().BeTrue();
            request.DestinationPolicy.Allows(new Uri("https://cdn.example.evil.test/x.jpg")).Should().BeFalse();
            request.DestinationPolicy.Allows(new Uri("https://evilcdn.example/x.jpg")).Should().BeFalse();
            request.DestinationPolicy.Allows(new Uri("https://img.cdn.example:8443/x.jpg")).Should().BeFalse();
            request.DestinationPolicy.Allows(new Uri("https://127.0.0.1/x.jpg")).Should().BeFalse();
        }

        JsonElement posts = await DataAsync(await fixture.Client.GetAsync($"/api/sources/{lane.Source}/posts"));
        string[] images = posts.EnumerateArray().Single(post => post.GetProperty("imageUrls").GetArrayLength() == 4)
            .GetProperty("imageUrls").EnumerateArray().Select(value => value.GetString()!).ToArray();
        images.Take(2).Should().OnlyContain(url => url.StartsWith($"/api/sources/{lane.Source}/assets/", StringComparison.Ordinal));
        images.Skip(2).Should().Equal(original[2], original[3]);
        using var image = await fixture.Client.GetAsync(images[0]);
        image.StatusCode.Should().Be(HttpStatusCode.OK);
        await store.QueueMissingAsync(default);
        (await store.ClaimAsync(default)).Should().BeNull();
    }

    [Fact]
    public async Task A_declared_image_domain_covers_its_apex_and_every_subdomain_without_regard_to_case_and_keeps_the_source_host()
    {
        await using var fixture = await Fixture.CreateAsync(new MediaAdapter("shop.example.test", new SourceMediaHosts { Domains = ["cdn.example"] }));
        var lane = await fixture.SourceAsync("site", "shop.example.test", "ownFetch");
        string[] queued = ["https://cdn.example/a.jpg", "https://IMG.CDN.EXAMPLE/b.jpg", "https://deep.img.cdn.example/c.jpg", "https://shop.example.test/own.jpg"];
        await fixture.CaptureAsync(lane, [.. queued, "https://cdn.example.evil.test/d.jpg", "https://notcdn.example/e.jpg"]);
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(4);
        using var worker = fixture.Worker();
        while (await store.ClaimAsync(default) is { } asset) await worker.CopyAsync(asset, default);
        fixture.Files.Uploads.Should().HaveCount(4);
        // The source's own host keeps the host's pacing; only the declared domain's images carry the declared gap.
        fixture.Fetcher.Requests.Where(request => request.Uri.Host == "shop.example.test").Should().ContainSingle().Which.MinDelay.Should().BeNull();
        fixture.Fetcher.Requests.Where(request => request.Uri.Host != "shop.example.test").Should().HaveCount(3)
            .And.OnlyContain(request => request.MinDelay == TimeSpan.FromSeconds(2) && request.RobotsPolicy == RobotsPolicy.Obey);
    }

    [Fact]
    public async Task A_site_source_without_declared_media_still_queues_only_its_own_host_images()
    {
        await using var fixture = await Fixture.CreateAsync();
        var plain = await fixture.SourceAsync("site", "plain.example.test", "ownFetch");
        await fixture.CaptureAsync(plain, "https://img.cdn.example/1.jpg", "https://plain.example.test/own.png", "https://olxcdn.com/2.jpg");
        var store = fixture.Host.Services.GetRequiredService<SourceAssetStore>();
        await store.QueueMissingAsync(default);
        (await fixture.ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets")).Should().Be(1);
        var asset = (await store.ClaimAsync(default))!;
        asset.Url.Should().Be("https://plain.example.test/own.png");
        await fixture.Worker().CopyAsync(asset, default);
        fixture.Fetcher.Requests.Should().ContainSingle().Which.MinDelay.Should().BeNull();
        fixture.Fetcher.Requests.Single().RobotsPolicy.Should().Be(RobotsPolicy.Obey);
        fixture.Fetcher.Requests.Single().DestinationPolicy!.Allows(new Uri("https://img.cdn.example/1.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task The_last_installed_adapter_that_serves_a_source_decides_its_media_rules_and_they_are_read_once_a_minute()
    {
        var declared = new SourceMediaHosts { Domains = [".CDN.Example", "localhost", "not a host.example"], Limit = 3, MinDelay = TimeSpan.FromSeconds(5) };
        await using var fixture = await Fixture.CreateAsync(
            new MediaAdapter("shadowed.example.test", declared), new MediaAdapter("shadowed.example.test", null),
            new MediaAdapter("shop.example.test", null), new MediaAdapter("shop.example.test", declared));
        var shadowed = await fixture.SourceAsync("site", "shadowed.example.test", "ownFetch");
        var shop = await fixture.SourceAsync("site", "shop.example.test", "ownFetch");
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        var rules = new SourceMediaRules(fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            fixture.Host.Services.GetServices<ISourceAdapter>(), clock);

        var found = await rules.ListAsync(default);
        found.Should().ContainSingle().Which.Should().BeEquivalentTo(new { SourceId = Guid.Parse(shop.Source), Domains = new[] { "cdn.example" }, Limit = 3, MinDelay = TimeSpan.FromSeconds(5) });
        (await rules.FindAsync(Guid.Parse(shadowed.Source), default)).Should().BeNull();

        // The answer is cached for one scan interval, then resolved again.
        var late = await fixture.SourceAsync("site", "shop.example.test", "ownFetch");
        clock.Advance(TimeSpan.FromSeconds(59));
        (await rules.ListAsync(default)).Should().HaveCount(1);
        clock.Advance(TimeSpan.FromSeconds(2));
        (await rules.ListAsync(default)).Select(rule => rule.SourceId).Should().BeEquivalentTo([Guid.Parse(shop.Source), Guid.Parse(late.Source)]);

        // A paused source or a denied one holds no rule.
        await fixture.ExecuteAsync("UPDATE harvest_sources SET state='paused' WHERE id=@source", ("source", Guid.Parse(late.Source)));
        await fixture.ExecuteAsync("UPDATE harvest_sources SET permission='denied' WHERE id=@source", ("source", Guid.Parse(shop.Source)));
        clock.Advance(TimeSpan.FromMinutes(2));
        (await rules.ListAsync(default)).Should().BeEmpty();
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response)
        {
            string body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("data").Clone();
        }
    }

    private sealed record Lane(string Tenant, string Container, string Source);

    private sealed class Fixture(WebApplication host, string database, FixtureFetcher fetcher, FixtureFiles files) : IAsyncDisposable
    {
        public WebApplication Host { get; } = host;
        public FixtureFetcher Fetcher { get; } = fetcher;
        public FixtureFiles Files { get; } = files;
        public HttpClient Client { get; } = ClientFor(host, "hv_SourceAssetOperatorFixture000000");

        public static async Task<Fixture> CreateAsync(params ISourceAdapter[] adapters)
        {
            string database = await SourcePostgres.NewDatabaseAsync();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(Program).Assembly.GetName().Name, EnvironmentName = "Development" });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = database,
                ["Harvest:Operator:ApiKey"] = "hv_SourceAssetOperatorFixture000000",
                ["Harvest:Worker:Enabled"] = "false",
                ["HarvestAssets:Files:BaseAddress"] = "https://files.example.test/",
                ["AllowedHosts"] = "localhost", ["Logging:LogLevel:Default"] = "Warning",
            });
            builder.WebHost.UseTestServer();
            builder.Configure();
            // Drive the actual durable worker one item at a time, without timers or any outbound HTTP.
            foreach (var service in builder.Services.Where(service => service.ServiceType == typeof(IHostedService)
                && service.ImplementationType == typeof(SourceAssetWorker)).ToArray()) builder.Services.Remove(service);
            var fetcher = new FixtureFetcher();
            var files = new FixtureFiles();
            builder.Services.RemoveAll<IPageFetcher>();
            builder.Services.RemoveAll<IFileUploadsBroker>();
            builder.Services.RemoveAll<IFileStoreBroker>();
            builder.Services.AddSingleton<IPageFetcher>(fetcher).AddSingleton<IFileUploadsBroker>(files).AddSingleton<IFileStoreBroker>(files);
            // Added after the host's own adapters, so the last one that serves a source is the fixture's.
            foreach (var adapter in adapters) builder.Services.AddSingleton(adapter);
            var host = builder.Build();
            await host.ConfigureAsync();
            await host.StartAsync();
            return new Fixture(host, database, fetcher, files);
        }

        public SourceAssetWorker Worker() => new(Host.Services.GetRequiredService<SourceAssetStore>(), Host.Services.GetRequiredService<SourceMediaRules>(),
            Fetcher, Files, TimeProvider.System, NullLogger<SourceAssetWorker>.Instance);

        public async Task<Lane> SourceAsync(string family, string handle, string posture)
        {
            var tenant = await DataAsync(await Client.PostAsJsonAsync("/api/tenants", new { slug = "assets-" + Guid.NewGuid().ToString("N"), name = "Assets" }));
            string tenantId = tenant.GetProperty("id").GetString()!;
            var container = await DataAsync(await Client.PostAsJsonAsync("/api/containers", new { kind = "private", tenantId }));
            string containerId = container.GetProperty("id").GetString()!;
            var source = await DataAsync(await Client.PostAsJsonAsync("/api/sources", new { containerId, family, handle, posture, coverage = "filtered", completeness = "window" }));
            return new Lane(tenantId, containerId, source.GetProperty("id").GetString()!);
        }

        public async Task CaptureAsync(Lane lane, params string[] images)
        {
            string url = "https://www.instagram.com/p/" + Guid.NewGuid().ToString("N") + "/";
            await DataAsync(await Client.PostAsJsonAsync("/api/posts/captures", new
            {
                containerId = lane.Container, sourceId = lane.Source,
                capture = new { externalKey = url, url, requestUrl = url, finalUrl = url, httpStatus = 200, capturedAtUtc = DateTimeOffset.UtcNow,
                    body = Convert.ToBase64String(Encoding.UTF8.GetBytes(url)), contentProjection = url, contentType = "text/plain" },
                envelope = new { schema = "wow2.post.v1", kind = "other", title = url, text = url,
                    media = images.Select(image => new { kind = "image", url = image }).ToArray(),
                    extraction = new { normVersion = "norm@1", rulesVersion = "rules@1" } },
            }));
        }

        public const string OffKey = "ocharo.assets-off";

        /// <summary>Stamps the source with a catalog entry and records the entry as turned off.</summary>
        public async Task TurnOffAsync(Lane lane) => await ExecuteAsync("""
            UPDATE harvest_sources SET catalog_key=@key WHERE id=@source;
            INSERT INTO harvest_catalog_sources(key,turned_on,changed_by) VALUES (@key,false,'subject-1') ON CONFLICT (key) DO UPDATE SET turned_on=false
            """, ("key", OffKey), ("source", Guid.Parse(lane.Source)));

        public async Task<HttpClient> ReaderAsync(string tenant, string scope)
        {
            var key = await DataAsync(await Client.PostAsJsonAsync($"/api/tenants/{tenant}/api-keys", new { name = "Asset reader", scopes = new[] { scope } }));
            return ClientFor(Host, key.GetProperty("secret").GetString()!);
        }

        public async Task ExecuteAsync(string sql, params (string Key, object Value)[] parameters)
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (key, value) in parameters) command.Parameters.AddWithValue(key, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        private static HttpClient ClientFor(WebApplication host, string key)
        {
            var client = host.GetTestClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
            return client;
        }

        public async ValueTask DisposeAsync() { Client.Dispose(); await Host.DisposeAsync(); }
    }

    /// <summary>Serves one source handle and declares the media hosts its images may be copied from.</summary>
    private sealed class MediaAdapter(string handle, SourceMediaHosts? media) : ISourceAdapter
    {
        public string Key { get; } = "media-fixture-" + Guid.NewGuid().ToString("N");
        public SourceFamily Family => SourceFamily.Site;
        public bool Serves(SourceModel source) => source.Family == Family && source.Handle == handle;
        public AdapterDescriptor Describe(SourceModel source) => new()
        {
            Media = media, Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>(),
        };
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class FixtureFetcher : IPageFetcher
    {
        public static byte[] Png { get; } = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZfoAAAAASUVORK5CYII=");
        public List<FetchRequest> Requests { get; } = [];
        public string ContentType { get; set; } = "image/png";
        public byte[] Body { get; set; } = Png;
        public Uri? FinalUrl { get; set; }
        public FetchOutcome Outcome { get; set; } = FetchOutcome.Ok;
        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Outcome != FetchOutcome.Ok)
                return Task.FromResult(new FetchResponse { Outcome = Outcome });
            return Task.FromResult(new FetchResponse { Outcome = FetchOutcome.Ok, Document = new FetchedDocument
            {
                RequestUrl = request.Uri, FinalUrl = FinalUrl ?? request.Uri, StatusCode = 200, ContentType = ContentType,
                Body = Body, FetchedAtUtc = DateTimeOffset.UtcNow,
            } });
        }
    }

    private sealed class FixtureFiles : IFileUploadsBroker, IFileStoreBroker
    {
        public List<UploadSpec> Uploads { get; } = [];
        public bool Declines { get; set; }
        private readonly Dictionary<FileId, byte[]> content = [];
        public async Task<Result<FileModel>> UploadAsync(Stream stream, UploadSpec spec, CancellationToken cancellationToken = default)
        {
            if (Declines)
                return Result<FileModel>.Fail(AppErrorFactory.Validation("The Files fixture declined the upload."));
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes, cancellationToken);
            var id = new FileId(Guid.NewGuid());
            content[id] = bytes.ToArray();
            Uploads.Add(spec);
            return Result<FileModel>.Ok(new FileModel
            {
                Id = id, TenantId = "fixture", Name = spec.Name, ContentType = spec.ContentType!, Kind = FileKind.Image,
                Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(content[id])), Visibility = spec.Visibility,
                State = FileState.Ready, CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        public Task<Result<FileContent>> OpenReadAsync(FileId id, ByteRange? range = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<FileContent>.Ok(new FileContent(new MemoryStream(content[id]), "image/png", content[id].Length, content[id].Length, null)));
        public Task<Result<FileModel>> GetAsync(FileId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FilePage>> ListAsync(FileQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileModel>> UpdateAsync(FileId id, FilePatch patch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileModel>> DeleteAsync(FileId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<FileModel>> RestoreAsync(FileId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
