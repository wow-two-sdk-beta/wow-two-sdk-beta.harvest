using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace Harvest.Tests.Integration.Posts;

[Collection("Post delivery transactions")]
public sealed class PostMediaRefreshTests
{
    [Fact]
    public async Task Unchanged_capture_recovers_initially_missing_media_and_records_its_provenance()
    {
        await using var host = await PostHost.StartAsync();
        PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        var capture = PostStoreTests.Capture(host, "missing-media", "A stable caption initially returned without a usable image URL.");
        var envelope = PostStoreTests.Envelope("Missing image", capture.ContentProjection) with { Media = [] };
        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, envelope));
        first.Post.Base.Media.Should().BeEmpty();
        first.Post.FieldSources.Should().NotContainKey("media");
        host.Clock.Advance(TimeSpan.FromHours(1));

        var recovered = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { CapturedAtUtc = host.Clock.GetUtcNow() }, envelope with { Media = Media("available") }));

        recovered.Changed.Should().BeFalse();
        recovered.CaptureId.Should().BeNull();
        recovered.Post.Base.Media.Should().Equal(Media("available"));
        recovered.Post.FieldSources["media"].Should().Be(first.LinkId);
        recovered.Post.Revision.Should().Be(first.Post.Revision);
        recovered.Post.LastChangedAtUtc.Should().Be(first.Post.LastChangedAtUtc);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event")).Should().Be(1);
    }

    [Fact]
    public async Task Unchanged_capture_refreshes_signed_media_without_revision_or_delivery_and_rejects_older_references()
    {
        await using var host = await PostHost.StartAsync();
        PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        var capture = PostStoreTests.Capture(host, "signed-media", "A stable caption whose signed media URL changes independently.");
        var envelope = PostStoreTests.Envelope("Signed image", capture.ContentProjection) with { Media = Media("old") };
        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, envelope));
        host.Clock.Advance(TimeSpan.FromHours(2));
        var refreshed = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { CapturedAtUtc = host.Clock.GetUtcNow() }, envelope with { Media = Media("fresh") }));

        refreshed.Changed.Should().BeFalse();
        refreshed.CaptureId.Should().BeNull();
        refreshed.Post.Base.Media.Should().Equal(Media("fresh"));
        refreshed.Post.Revision.Should().Be(first.Post.Revision);
        refreshed.Post.LastChangedAtUtc.Should().Be(first.Post.LastChangedAtUtc);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event")).Should().Be(1);

        var older = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { CapturedAtUtc = capture.CapturedAtUtc.AddHours(1) }, envelope));
        older.Post.Base.Media.Should().Equal(Media("fresh"));
    }

    [Fact]
    public async Task Refresh_updates_only_the_link_that_supplies_the_resolved_media()
    {
        await using var host = await PostHost.StartAsync();
        Guid authoritative = await host.SourceAsync("organizer.example", authority: 90);
        var capture = PostStoreTests.Capture(host, "shared-image", "A shared event with independently refreshed media references.")
            with { OriginKey = "https://organizer.example/event/42" };
        var envelope = PostStoreTests.Envelope("Event", capture.ContentProjection) with { Media = Media("secondary") };
        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, envelope));
        var primaryCapture = capture with { Url = "https://organizer.example/shared-image" };
        var primary = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, authoritative, null,
            primaryCapture, envelope with { Media = Media("primary") }));
        primary.Post.Id.Should().Be(first.Post.Id);
        primary.Post.FieldSources["media"].Should().Be(primary.LinkId);
        host.Clock.Advance(TimeSpan.FromHours(1));

        var secondary = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { CapturedAtUtc = host.Clock.GetUtcNow() }, envelope with { Media = Media("secondary-fresh") }));
        secondary.Post.Base.Media.Should().Equal(Media("primary"));
        var refreshed = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, authoritative, null,
            primaryCapture with { CapturedAtUtc = host.Clock.GetUtcNow() }, envelope with { Media = Media("primary-fresh") }));
        refreshed.Post.Base.Media.Should().Equal(Media("primary-fresh"));
        refreshed.Post.Revision.Should().Be(primary.Post.Revision);
    }

    private static PostMedia[] Media(string signature) =>
        [new() { Kind = MediaKind.Image, Url = "https://images.cdninstagram.com/image.jpg?signature=" + signature }];
}
