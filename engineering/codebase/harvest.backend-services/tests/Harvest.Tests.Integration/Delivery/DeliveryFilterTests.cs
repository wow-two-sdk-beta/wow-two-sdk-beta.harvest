using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Delivery;

[Collection("Post delivery transactions")]
public sealed class DeliveryFilterTests
{
    private const string FilterKey = "test-title-v1";

    [Fact]
    public async Task Capture_ShouldFilterDeliveryAndKeepRawPosts_WhenSubscriptionsHaveDifferentInterests()
    {
        await using var host = await PostHost.StartAsync();
        await using var filtered = Open(host, new TitleFilter());
        var subscription = PostHost.Value(await filtered.Delivery.SubscribeAsync(Declaration(host) with { Backfill = BackfillMode.None }));
        var unfiltered = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host) with { FilterKey = null }));
        var concert = PostStoreTests.Capture(host, "concert", "A concert with live music.");
        var excluded = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, concert,
            PostStoreTests.Envelope("Evening concert", concert.ContentProjection)));
        var fashion = PostStoreTests.Capture(host, "fashion", "A fashion exhibition for designers and clothing sellers.");
        var included = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, fashion,
            PostStoreTests.Envelope("Fashion exhibition", fashion.ContentProjection)));
        var repeated = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, fashion,
            PostStoreTests.Envelope("Fashion exhibition", fashion.ContentProjection)));
        repeated.Changed.Should().BeFalse();
        host.Clock.Advance(TimeSpan.FromHours(1));
        var changed = fashion with { ContentProjection = "Fashion exhibition with an updated programme.", CapturedAtUtc = host.Clock.GetUtcNow() };
        PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, changed,
            PostStoreTests.Envelope("Fashion exhibition updated", changed.ContentProjection)));

        var first = await host.PullUntilAsync(subscription.Id, page => page.HasMore, limit: 1);
        first.Data.Should().ContainSingle(); first.Data[0].PostId.Should().Be(included.Post.Id);
        first.Data[0].Context["catalog"].Should().Be("opaque-routing-data");
        var second = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 1, first.Next, limit: 1);
        second.Data[0].Revision.Should().Be(2);
        var replay = PostHost.Value(await filtered.Delivery.PullAsync(subscription.Id, limit: 1));
        replay.Data[0].EventId.Should().Be(first.Data[0].EventId);
        var empty = PostHost.Value(await filtered.Delivery.PullAsync(subscription.Id, second.Next));
        empty.Data.Should().BeEmpty(); empty.Next.Should().Be(second.Next);
        var all = await host.PullUntilAsync(unfiltered.Id, page => page.Data.Count == 3);
        all.Data.Select(item => item.PostId).Should().Contain(excluded.Post.Id);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_posts")).Should().Be(2);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(3);
        (await host.ScalarAsync<byte[]>("SELECT body_inline FROM harvest_captures WHERE id=@id", ("id", excluded.CaptureId!.Value)))
            .Should().Equal(concert.Body.ToArray());
        PostHost.Value(await filtered.Delivery.GetSubscriptionAsync(subscription.Id)).FilterKey.Should().Be(FilterKey);
        PostHost.Value(await filtered.Delivery.ListSubscriptionsAsync(host.TenantId)).Single(item => item.Id == subscription.Id)
            .FilterKey.Should().Be(FilterKey);
    }

    [Fact]
    public async Task Subscribe_ShouldBackfillOnlyMatchingPosts_WhenTheyWereCapturedBeforeSubscription()
    {
        await using var host = await PostHost.StartAsync();
        foreach (var title in new[] { "Concert", "Fashion market" })
        {
            var capture = PostStoreTests.Capture(host, title.Replace(' ', '-'), title);
            PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, PostStoreTests.Envelope(title, title)));
        }
        await using var filtered = Open(host, new TitleFilter());
        var subscription = PostHost.Value(await filtered.Delivery.SubscribeAsync(Declaration(host)));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 1);

        page.Data.Should().ContainSingle(); page.Data[0].Reason.Should().Be("backfill");
        page.Data[0].Data!.Value.GetProperty("base").GetProperty("title").GetString().Should().Be("Fashion market");
        var replay = PostHost.Value(await filtered.Delivery.PullAsync(subscription.Id));
        replay.Data[0].EventId.Should().Be(page.Data[0].EventId);
        var empty = PostHost.Value(await filtered.Delivery.PullAsync(subscription.Id, page.Next));
        empty.Data.Should().BeEmpty(); empty.Next.Should().Be(page.Next);
    }

    [Fact]
    public async Task Subscribe_ShouldRejectUnknownFilter_WhenTheHostCannotResolveIt()
    {
        await using var host = await PostHost.StartAsync();

        var result = await host.Delivery.SubscribeAsync(Declaration(host));

        result.Should().BeOfType<Result<SubscriptionModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Validation);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscriptions")).Should().Be(0);
    }

    [Fact]
    public async Task Capture_ShouldFailClosedAndStillDeliverLifecycle_WhenTheConfiguredFilterDisappearsAfterRestart()
    {
        await using var host = await PostHost.StartAsync();
        var original = PostStoreTests.Capture(host, "known", "Fashion market before the host restarts.");
        SubscriptionModel subscription;
        CaptureSaveResult saved;
        await using (var filtered = Open(host, new TitleFilter()))
        {
            subscription = PostHost.Value(await filtered.Delivery.SubscribeAsync(Declaration(host)));
            saved = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, original,
                PostStoreTests.Envelope("Fashion market", original.ContentProjection)));
        }
        host.Clock.Advance(TimeSpan.FromHours(1));
        var unseen = PostStoreTests.Capture(host, "unseen", "A new fashion event after restart.");
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, unseen,
            PostStoreTests.Envelope("Fashion show", unseen.ContentProjection)));
        var changed = original with { CapturedAtUtc = host.Clock.GetUtcNow(), ContentProjection = "A changed fashion event after restart." };
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, changed,
            PostStoreTests.Envelope("Fashion market updated", changed.ContentProjection)));
        PostHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId,
            new LinkObservation { ExternalKey = original.ExternalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410 }));
        host.Clock.Advance(TimeSpan.FromHours(1));
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            changed with { CapturedAtUtc = host.Clock.GetUtcNow() }, PostStoreTests.Envelope("Fashion market updated", changed.ContentProjection)));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 3);

        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.LinkState, DeliveryEventTypes.PostChanged);
        page.Data.Should().OnlyContain(item => item.PostId == saved.Post.Id);
        page.Data[1].Data!.Value.GetProperty("state").GetString().Should().Be("gone");
        page.Data[2].Data!.Value.GetProperty("state").GetString().Should().Be("live");
        PostHost.Value(await host.Delivery.GetSubscriptionAsync(subscription.Id)).FilterKey.Should().Be(FilterKey);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(3);
    }

    [Fact]
    public async Task ObserveAndMerge_ShouldKeepConsumerStateCurrent_WhenAnEarlierPostNoLongerMatches()
    {
        await using var host = await PostHost.StartAsync();
        await using var filtered = Open(host, new TitleFilter());
        var subscription = PostHost.Value(await filtered.Delivery.SubscribeAsync(Declaration(host)));
        var capture = PostStoreTests.Capture(host, "known", "Fashion market originally announced here.");
        var original = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture,
            PostStoreTests.Envelope("Fashion market", capture.ContentProjection)));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var changed = capture with { CapturedAtUtc = host.Clock.GetUtcNow(), ContentProjection = "This is now a concert announcement." };
        PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, changed,
            PostStoreTests.Envelope("Concert", changed.ContentProjection)));
        PostHost.Value(await filtered.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId,
            new LinkObservation { ExternalKey = capture.ExternalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410 }));
        var survivorCapture = PostStoreTests.Capture(host, "survivor", "Another concert announcement.");
        var survivor = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, survivorCapture,
            PostStoreTests.Envelope("Another concert", survivorCapture.ContentProjection)));
        PostHost.Value(await filtered.Posts.MergeAsync(host.ContainerId, survivor.Post.Id, original.Post.Id));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 3);

        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.LinkState, DeliveryEventTypes.PostMerged);
        page.Data.Should().OnlyContain(item => item.PostId == original.Post.Id);
        page.Data[2].Data!.Value.GetProperty("mergedInto").GetGuid().Should().Be(survivor.Post.Id);
    }

    [Fact]
    public async Task Capture_ShouldKeepRawEvidenceAndWithholdDelivery_WhenTheRegisteredFilterThrows()
    {
        await using var host = await PostHost.StartAsync();
        await using var filtered = Open(host, new ThrowingFilter());
        var subscription = PostHost.Value(await filtered.Delivery.SubscribeAsync(Declaration(host)));
        var capture = PostStoreTests.Capture(host, "failure", "Raw evidence survives a faulty delivery extension.");

        var saved = PostHost.Value(await filtered.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture,
            PostStoreTests.Envelope("Fashion market", capture.ContentProjection)));

        saved.Changed.Should().BeTrue();
        PostHost.Value(await filtered.Delivery.PullAsync(subscription.Id)).Data.Should().BeEmpty();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_posts")).Should().Be(1);
    }

    private static SubscriptionDeclaration Declaration(PostHost host) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, FilterKey = FilterKey,
        Context = new Dictionary<string, string> { ["catalog"] = "opaque-routing-data" },
    };

    private static FilterScope Open(PostHost host, IDeliveryPostFilter filter)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(host.Clock).AddSingleton(filter);
        services.AddHarvest().Configure(options => options.ConnectionString = host.ConnectionString);
        services.AddHarvestPosts().AddHarvestDelivery();
        var provider = services.BuildServiceProvider(validateScopes: true);
        return new FilterScope(provider, provider.CreateAsyncScope());
    }

    private sealed class FilterScope(ServiceProvider provider, AsyncServiceScope scope) : IAsyncDisposable
    {
        internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
        internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        public async ValueTask DisposeAsync() { await scope.DisposeAsync(); await provider.DisposeAsync(); }
    }

    private sealed class TitleFilter : IDeliveryPostFilter
    {
        public string Key => FilterKey;
        public bool Matches(PostEnvelope envelope) => envelope.Title?.Contains("fashion", StringComparison.OrdinalIgnoreCase) is true;
    }

    private sealed class ThrowingFilter : IDeliveryPostFilter
    {
        public string Key => FilterKey;
        public bool Matches(PostEnvelope envelope) => throw new InvalidOperationException("Filter failed.");
    }
}
