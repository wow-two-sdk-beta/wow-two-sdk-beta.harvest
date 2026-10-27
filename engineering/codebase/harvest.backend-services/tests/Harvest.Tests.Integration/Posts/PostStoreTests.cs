using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Posts;

[Collection("Post delivery transactions")]
public sealed class PostStoreTests
{
    [Fact]
    public async Task ReextractingASavedCapture_ShouldNotClaimANewAvailabilityCheck()
    {
        await using var host = await PostHost.StartAsync();
        var capture = Capture(host, "reextract", "An unchanged source snapshot.");
        host.Clock.Advance(TimeSpan.FromHours(1));
        var saved = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture,
            Envelope("Original extraction", capture.ContentProjection)));
        host.Clock.Advance(TimeSpan.FromHours(12));

        var replay = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { ContentProjection = "New parser projection of the same saved source bytes." },
            Envelope("Corrected extraction", "Complete source text")));

        replay.Post.Id.Should().Be(saved.Post.Id);
        replay.Post.Revision.Should().Be(2);
        replay.Post.Links.Single().CapturedAtUtc.Should().Be(capture.CapturedAtUtc);
        replay.Post.Links.Single().LastSeenAtUtc.Should().Be(capture.CapturedAtUtc);
        replay.Post.Links.Single().LastCheckedAtUtc.Should().BeNull();
        replay.Post.Links.Single().CheckCount.Should().Be(0);
        replay.Post.Links.Single().NextCheckEligibleAtUtc.Should().Be(capture.CapturedAtUtc.AddHours(6));
    }

    [Fact]
    public async Task Query_ShouldExposeAvailabilityEvidenceSeparatelyFromTheContentCapture()
    {
        await using var host = await PostHost.StartAsync();
        var capture = Capture(host, "checks", "A source post with immutable content.");
        var saved = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture,
            Envelope("Checks", capture.ContentProjection) with
            {
                Extras = new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["source"] = System.Text.Json.JsonSerializer.SerializeToElement(new { schema = "fixture.v1", fields = new { sourceOnly = true } }),
                },
            }));
        saved.Post.Links.Single().LastCheckedAtUtc.Should().BeNull();
        host.Clock.Advance(TimeSpan.FromHours(1));
        var observation = new LinkObservation
        {
            ExternalKey = capture.ExternalKey, Signal = LinkSignal.Present, Reason = "seen_again", HttpStatus = 304,
        };

        PostHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId, observation));
        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        var link = post.Links.Single();

        link.CapturedAtUtc.Should().Be(capture.CapturedAtUtc);
        link.LastCheckedAtUtc.Should().Be(host.Clock.GetUtcNow());
        link.LastSeenAtUtc.Should().Be(host.Clock.GetUtcNow());
        link.NextCheckEligibleAtUtc.Should().Be(host.Clock.GetUtcNow().AddHours(6));
        link.LastCheckOutcome.Should().Be("Present");
        link.LastCheckHttpStatus.Should().Be(304);
        link.CheckCount.Should().Be(1);
        link.SourceFields!.Value.GetProperty("fields").GetProperty("sourceOnly").GetBoolean().Should().BeTrue();
        link.LastCheckRunId.Should().BeNull();
        post.Revision.Should().Be(1);
    }

    [Fact]
    public async Task SaveCapture_ShouldWriteOneCaptureAndEvent_WhenAClaimIsReplayed()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        var capture = Capture(host, "concert", "A live concert with enough descriptive text to distinguish this event from other events.");
        var envelope = Envelope("A concert", capture.ContentProjection);

        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, envelope));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var replay = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture with { CapturedAtUtc = host.Clock.GetUtcNow() }, envelope));

        first.Changed.Should().BeTrue(); replay.Changed.Should().BeFalse(); replay.CaptureId.Should().BeNull();
        replay.Post.Id.Should().Be(first.Post.Id); replay.Post.Revision.Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_captures")).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event")).Should().Be(1);
        (await host.ScalarAsync<byte[]>("SELECT body_inline FROM harvest_captures")).Should().Equal(capture.Body.ToArray());
        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count == 1);
        page.Data.Should().ContainSingle(); page.Data[0].Revision.Should().Be(1);
    }

    [Fact]
    public async Task SaveCapture_ShouldResolveFieldsByAuthority_WhenOriginsIdentifyTheSamePost()
    {
        await using var host = await PostHost.StartAsync();
        var secondSource = await host.SourceAsync("voyage.uz", authority: 90);
        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            Capture(host, "event", "Original organizer event text") with { OriginKey = "https://organizer.example/event/42" }, Envelope("Original title", "Original body") with
            {
                FieldSources = new Dictionary<string, FieldSource>
                {
                    ["title"] = new() { Tier = ExtractionTier.Rules, Rule = "lower-title" },
                    ["text"] = new() { Tier = ExtractionTier.Rules, Rule = "lower-text" },
                },
            }));
        var second = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, secondSource, null,
            Capture(host, "listing", "Corrected organizer event text", "voyage.uz") with { OriginKey = "https://organizer.example/event/42" }, Envelope("Corrected title", "Corrected body") with
            {
                Text = null,
                FieldSources = new Dictionary<string, FieldSource> { ["title"] = new() { Tier = ExtractionTier.Rules, Rule = "higher-title" } },
            }));

        second.Post.Id.Should().Be(first.Post.Id);
        second.Post.Links.Should().HaveCount(2);
        second.Post.Base.Title.Should().Be("Corrected title");
        second.Post.FieldSources["title"].Should().Be(second.LinkId);
        second.Post.FieldSources["text"].Should().Be(first.LinkId);
        second.Post.Base.Text.Should().Be("Original body");
        second.Post.Base.FieldSources["title"].Rule.Should().Be("higher-title");
        second.Post.Base.FieldSources["text"].Rule.Should().Be("lower-text");
        second.Post.Revision.Should().Be(2);
    }

    [Fact]
    public async Task SaveCapture_ShouldKeepStructuredEventsApart_WhenTheyShareAPageButHaveDistinctOrigins()
    {
        await using var host = await PostHost.StartAsync();
        var capture = Capture(host, "shared", "An event published on a shared calendar page with the same repeated template body.");
        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { ExternalKey = "event-one", OriginKey = "urn:event:one" }, Envelope("Same title", capture.ContentProjection)));
        var second = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            capture with { ExternalKey = "event-two", OriginKey = "urn:event:two" }, Envelope("Same title", capture.ContentProjection)));

        second.Post.Id.Should().NotBe(first.Post.Id);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_posts")).Should().Be(2);
    }

    [Fact]
    public async Task Observe_ShouldRequireAnother404AndRestoreGonePosts_WhenCheckedAgain()
    {
        await using var host = await PostHost.StartAsync();
        var capture = Capture(host, "deleted", "The event that disappears.");
        var original = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, Envelope("Delete me", capture.ContentProjection)));
        var missing = new LinkObservation { ExternalKey = capture.ExternalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_404", HttpStatus = 404 };

        var suspect = PostHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId, missing));
        suspect.State.Should().Be(PostState.Suspect);
        host.Clock.Advance(TimeSpan.FromHours(6));
        var gone = PostHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId, missing));
        gone.State.Should().Be(PostState.Gone);
        host.Clock.Advance(TimeSpan.FromHours(6));
        var restored = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture with { CapturedAtUtc = host.Clock.GetUtcNow() }, Envelope("Delete me", capture.ContentProjection)));
        restored.Changed.Should().BeFalse(); restored.Post.Id.Should().Be(original.Post.Id);
        restored.Post.State.Should().Be(PostState.Live); restored.Post.Links[0].State.Should().Be(LinkState.Relisted);
    }

    [Fact]
    public async Task Observe_ShouldAbstainFromRollup_WhenSourceHasNoExistenceChecks()
    {
        await using var host = await PostHost.StartAsync();
        var source = await host.SourceAsync("feed.example", checkPolicy: "none");
        var capture = Capture(host, "item", "A feed entry", "feed.example");
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, null, capture, Envelope("Feed", capture.ContentProjection)));
        var post = PostHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, source,
            new LinkObservation { ExternalKey = capture.ExternalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410 }));
        post.State.Should().Be(PostState.Live); post.Links[0].State.Should().Be(LinkState.Live);
    }

    [Fact]
    public async Task Unmerge_ShouldRestoreOriginalLinksAndPreventAutomaticMerge_WhenTheMergeWasWrong()
    {
        await using var host = await PostHost.StartAsync();
        var first = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            Capture(host, "one", "First event"), Envelope("First", "First event")));
        var second = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            Capture(host, "two", "Second event"), Envelope("Second", "Second event")));
        var merge = PostHost.Value(await host.Posts.MergeAsync(host.ContainerId, first.Post.Id, second.Post.Id));
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, first.Post.Id)).Links.Should().HaveCount(2);

        PostHost.Value(await host.Posts.UnmergeAsync(host.ContainerId, merge.Id)).Undone.Should().BeTrue();
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, first.Post.Id)).Links.Should().ContainSingle();
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, second.Post.Id)).Links.Should().ContainSingle();
        var changed = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            Capture(host, "two", "First event"), Envelope("First", "First event")));
        changed.Post.Id.Should().Be(second.Post.Id);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_keep_apart")).Should().Be(1);
    }

    [Fact]
    public async Task GetPost_ShouldReturnNotFound_WhenAnotherContainerIsUsed()
    {
        await using var host = await PostHost.StartAsync();
        var capture = Capture(host, "private", "Scoped post");
        var post = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null, capture, Envelope("Scoped", capture.ContentProjection)));
        (await host.Posts.GetPostAsync(Guid.NewGuid(), post.Post.Id)).Should().BeOfType<WoW.Two.Sdk.Beta.Be.Foundation.Results.Result<PostModel>.Failure>();
    }

    internal static Capture Capture(PostHost host, string key, string text, string domain = "mstar.uz") => new()
    {
        ExternalKey = key, Url = $"https://{domain}/{key}", RequestUrl = $"https://{domain}/{key}", FinalUrl = $"https://{domain}/{key}",
        CapturedAtUtc = host.Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, HttpStatus = 200, ContentType = "text/html",
    };

    internal static PostEnvelope Envelope(string title, string text) => new()
    {
        Kind = PostKind.Event, Title = title, Text = text, Language = new PostLanguage { Code = "en" },
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };
}
