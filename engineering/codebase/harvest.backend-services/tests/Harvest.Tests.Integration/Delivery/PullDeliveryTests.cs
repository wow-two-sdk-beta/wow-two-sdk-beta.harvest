using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Delivery;

[Collection("Post delivery transactions")]
public sealed class PullDeliveryTests
{
    [Fact]
    public async Task Pull_ShouldResumeAndReplayStableRevisions_WhenThePostChangedBeforeReading()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host)));
        var capture = PostStoreTests.Capture(host, "event", "First published description.");
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, PostStoreTests.Envelope("First", capture.ContentProjection)));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var changed = capture with { CapturedAtUtc = host.Clock.GetUtcNow(), ContentProjection = "Updated published description." };
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, changed, PostStoreTests.Envelope("Updated", changed.ContentProjection)));

        var page = await host.PullUntilAsync(subscription.Id, page => page.HasMore, limit: 1);
        page.HasMore.Should().BeTrue(); page.Data.Should().ContainSingle();
        page.Data[0].Revision.Should().Be(1);
        page.Data[0].Data!.Value.GetProperty("base").GetProperty("title").GetString().Should().Be("First");
        var second = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 1, page.Next, limit: 1);
        second.Data[0].Revision.Should().Be(2);
        second.Data[0].Data!.Value.GetProperty("base").GetProperty("title").GetString().Should().Be("Updated");
        var replay = PostHost.Value(await host.Delivery.PullAsync(subscription.Id, limit: 1));
        replay.Data[0].EventId.Should().Be(page.Data[0].EventId);
        var empty = PostHost.Value(await host.Delivery.PullAsync(subscription.Id, second.Next));
        empty.Data.Should().BeEmpty(); empty.Next.Should().Be(second.Next);
    }

    [Fact]
    public async Task Subscribe_ShouldBackfillLivePosts_WhenJoiningAfterCapture()
    {
        await using var host = await PostHost.StartAsync();
        var capture = PostStoreTests.Capture(host, "event", "An event captured before subscription.");
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, PostStoreTests.Envelope("Already here", capture.ContentProjection)));
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host)));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 1);

        page.Data.Should().ContainSingle(); page.Data[0].Reason.Should().Be("backfill");
        page.Data[0].Context["catalog"].Should().Be("events");
    }

    [Fact]
    public async Task Pull_ShouldWaitAtSnapshotHorizon_WhenAnEarlierTransactionCommitsLate()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host) with { Backfill = BackfillMode.None }));
        await using var earlier = new NpgsqlConnection(host.ConnectionString);
        await using var later = new NpgsqlConnection(host.ConnectionString);
        await earlier.OpenAsync(); await later.OpenAsync();
        await using var earlierTransaction = await earlier.BeginTransactionAsync();
        await InsertEventAsync(earlier, earlierTransaction, subscription.Id, "earlier", host.Clock.GetUtcNow());
        await using (var laterTransaction = await later.BeginTransactionAsync())
        {
            await InsertEventAsync(later, laterTransaction, subscription.Id, "later", host.Clock.GetUtcNow());
            await laterTransaction.CommitAsync();
        }

        var waiting = PostHost.Value(await host.Delivery.PullAsync(subscription.Id));
        waiting.Data.Should().BeEmpty();
        await earlierTransaction.CommitAsync();
        var resumed = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 2, waiting.Next);

        resumed.Data.Select(item => item.ItemId).Should().Equal("earlier", "later");
    }

    [Fact]
    public async Task Pull_ShouldRejectAnotherSubscriptionsCursor_WhenItIsReused()
    {
        await using var host = await PostHost.StartAsync();
        var first = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host)));
        var second = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host)));
        var page = PostHost.Value(await host.Delivery.PullAsync(first.Id));

        (await host.Delivery.PullAsync(second.Id, page.Next)).Should().BeOfType<Result<CursorPage<DeliveryEvent>>.Failure>();
    }

    [Fact]
    public async Task Subscribe_ShouldRejectAnotherTenantsPrivateContainer_WhenTenantDiffers()
    {
        await using var host = await PostHost.StartAsync();
        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var container = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));

        (await host.Delivery.SubscribeAsync(Declaration(host) with { ContainerId = container.Id }))
            .Should().BeOfType<Result<SubscriptionModel>.Failure>();
    }

    [Fact]
    public async Task Pull_ShouldProjectEventSchema_WhenSubscriberRequestsEvents()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host) with { Schema = "wow2.event.v1" }));
        var capture = PostStoreTests.Capture(host, "event", "An event description.");
        var envelope = PostStoreTests.Envelope("Event", capture.ContentProjection) with
        {
            Extras = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["event"] = System.Text.Json.JsonSerializer.SerializeToElement(new { schema = "wow2.event.v1", title = "Event" }),
            },
        };
        var post = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, envelope));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 1);

        page.Data[0].Data!.Value.GetProperty("schema").GetString().Should().Be("wow2.event.v1");
        page.Data[0].Data!.Value.GetProperty("postId").GetGuid().Should().Be(post.Post.Id);
        page.Data[0].Data!.Value.GetProperty("revision").GetInt32().Should().Be(1);
    }

    private static SubscriptionDeclaration Declaration(PostHost host) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        Context = new Dictionary<string, string> { ["catalog"] = "events" },
    };

    private static async Task InsertEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subscriptionId, string item, DateTimeOffset at)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO harvest_subscription_event(subscription_id,event_id,item_id,type,occurred_at,payload)
            VALUES(@subscription,@event,@item,'post.new',@at,'{}')
            """, connection, transaction);
        command.Parameters.AddWithValue("subscription", subscriptionId);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("item", item);
        command.Parameters.AddWithValue("at", at);
        await command.ExecuteNonQueryAsync();
    }
}
