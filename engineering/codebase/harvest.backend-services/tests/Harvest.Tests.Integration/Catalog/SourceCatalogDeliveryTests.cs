using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>Delivery for a source whose entry the instance turned off: no event is written for a post whose every link is an off
/// source, however the write is reached, and a selector change leaves the posts it already holds alone. Turning the entry on
/// resumes delivery.</summary>
[Collection("Post delivery transactions")]
public sealed class SourceCatalogDeliveryTests
{
    private static readonly string[] FashionTags = ["type:event", "domain:style-fashion", "topic:fashion-week"];
    private static readonly SubscriptionSelector Fashion = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] };
    private static readonly SubscriptionSelector Sports = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "sports" }] };

    [Fact]
    public async Task ChannelSubscription_ShouldReceiveNoEventForAnOffSource_NeitherFromIngestNorFromBackfill()
    {
        await using var host = await PostHost.StartAsync();
        var held = PostHost.Value(await host.Delivery.SubscribeAsync(Channel(host, BackfillMode.None)));
        var saved = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            PostStoreTests.Capture(host, "one", "A fair."), PostStoreTests.Envelope("One", "A fair.")));
        (await host.PullUntilAsync(held.Id, page => page.Data.Count >= 1)).Data.Should().ContainSingle().Which.Type.Should().Be(DeliveryEventTypes.PostNew);
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);

        await IngestAsync(host, saved.Post.Id, "post.changed");
        var late = PostHost.Value(await host.Delivery.SubscribeAsync(Channel(host, BackfillMode.Live) with { Name = null }));

        PostHost.Value(await host.Delivery.PullAsync(held.Id)).Data.Should().ContainSingle("the write for an off source's post appends nothing");
        PostHost.Value(await host.Delivery.PullAsync(late.Id)).Data.Should().BeEmpty("a replay writes no event for an off source's post");

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        await IngestAsync(host, saved.Post.Id, "post.changed");
        var page = await host.PullUntilAsync(held.Id, page => page.Data.Count >= 2);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostChanged);
    }

    [Fact]
    public async Task SelectorSubscription_ShouldReceiveNoEventForAnOffSource_AndAChangeUnmatchesNoHeldPost()
    {
        await using var host = await PostHost.StartAsync();
        var saved = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            PostStoreTests.Capture(host, "fw", "A fashion week."), Tagged("Fashion week", "A fashion week.", FashionTags)));
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Selector(host, "ocharo-events", Fashion)));
        (await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1)).Data.Should().ContainSingle().Which.PostId.Should().Be(saved.Post.Id);
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);

        var change = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports, ChangedBy = "operator" }));
        var later = PostHost.Value(await host.Delivery.SubscribeAsync(Selector(host, "second", Fashion)));
        await IngestAsync(host, saved.Post.Id, "post.changed");

        change.Changed.Should().BeTrue();
        change.Unmatched.Should().Be(0, "the selector scope never unmatches a post of an off source");
        PostHost.Value(await host.Delivery.PullAsync(subscription.Id)).Data.Should().ContainSingle().Which.Type.Should().Be(DeliveryEventTypes.PostNew);
        PostHost.Value(await host.Delivery.PullAsync(later.Id)).Data.Should().BeEmpty("a new selector subscription's backfill writes nothing for an off source");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE type='post.unmatched'")).Should().Be(0);

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        var resumed = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Fashion, ChangedBy = "operator" }));
        var away = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports, ChangedBy = "operator" }));
        resumed.Unmatched.Should().Be(0);
        away.Unmatched.Should().Be(1, "with the source on, the selector change unmatches its held post as before");
        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 2);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostUnmatched);
    }

    [Fact]
    public async Task Delivery_ShouldStillWrite_ForAPostWithALinkFromASourceThatIsOn()
    {
        await using var host = await PostHost.StartAsync();
        var other = await host.SourceAsync("other.uz");
        var held = PostHost.Value(await host.Delivery.SubscribeAsync(Channel(host, BackfillMode.None)));
        var saved = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            PostStoreTests.Capture(host, "shared", "A shared fair."), PostStoreTests.Envelope("Shared", "A shared fair.")));
        await host.PullUntilAsync(held.Id, page => page.Data.Count >= 1);
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);
        await CatalogSql.ExecuteAsync(host.ConnectionString, """
            INSERT INTO harvest_post_links(id,container_id,source_id,post_id,external_key,url,url_key,origin_key,role,state_since,first_seen_at,last_changed_at,check_capability)
            SELECT gen_random_uuid(),container_id,@other,post_id,'second-link',url || '/second',url_key,origin_key,role,state_since,first_seen_at,last_changed_at,check_capability
            FROM harvest_post_links WHERE post_id=@post
            """, ("other", other), ("post", saved.Post.Id));

        await IngestAsync(host, saved.Post.Id, "post.changed");

        (await host.PullUntilAsync(held.Id, page => page.Data.Count >= 2)).Data.Should().HaveCount(2,
            "a post another source still supplies is not a post whose every link is off");
    }

    private static async Task IngestAsync(PostHost host, Guid postId, string type)
    {
        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, postId));
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeliveryEventWriter.AppendAsync(connection, transaction, post, type, null, null, host.Clock.GetUtcNow().AddMinutes(1), new DeliveryPostFilters([]),
            CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static SubscriptionDeclaration Channel(PostHost host, BackfillMode backfill) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, Schema = "wow2.event.v1", Backfill = backfill,
    };

    private static SubscriptionDeclaration Selector(PostHost host, string name, SubscriptionSelector selector) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, Name = name, Selector = selector,
    };

    private static PostEnvelope Tagged(string title, string text, string[] tags) => PostStoreTests.Envelope(title, text) with
    {
        Tags = [.. tags.Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel })],
    };
}
