using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Specifies post and pull behavior shared by the in-process module, HTTP form and consumer fakes.</summary>
public abstract class PostDeliveryBrokerConformance
{
    /// <summary>Gets the registry that creates each case's isolated tenant and container.</summary>
    protected abstract IRegistrationBroker Registry { get; }
    /// <summary>Gets the post implementation under test.</summary>
    protected abstract IPostBroker Posts { get; }
    /// <summary>Gets the delivery implementation under test.</summary>
    protected abstract IDeliveryBroker Delivery { get; }
    /// <summary>Gets the implementation's clock.</summary>
    protected abstract TimeProvider Clock { get; }

    /// <summary>A repeated capture neither increments the post nor appends another delivery.</summary>
    [Fact]
    public async Task SaveCapture_ShouldRemainIdempotent_WhenRepeated()
    {
        var context = await ContextAsync();
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context)));
        var capture = Capture("one");
        var first = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, capture, Envelope("Original")));
        var repeated = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, capture, Envelope("Original")));
        repeated.Post.Id.Should().Be(first.Post.Id); repeated.Post.Revision.Should().Be(first.Post.Revision);
        repeated.Changed.Should().BeFalse(); repeated.CaptureId.Should().BeNull();
        var events = await PollAsync(subscription.Id, page => page.Data.Count == 1);
        events.Data.Should().ContainSingle(); events.Data[0].Type.Should().Be(DeliveryEventTypes.PostNew);
    }

    /// <summary>A pull cursor replays stable events while payloads retain their event revisions.</summary>
    [Fact]
    public async Task Pull_ShouldResumeStablePayloads_WhenThePostAdvances()
    {
        var context = await ContextAsync();
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context)));
        var capture = Capture("one");
        Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, capture, Envelope("Original")));
        Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null,
            capture with { ContentProjection = "Changed content", CapturedAtUtc = capture.CapturedAtUtc.AddSeconds(1) }, Envelope("Changed")));
        var first = await PollAsync(subscription.Id, page => page.HasMore, limit: 1);
        first.Data.Should().ContainSingle(); first.HasMore.Should().BeTrue(); first.Data[0].Revision.Should().Be(1);
        first.Data[0].Data!.Value.GetProperty("base").GetProperty("title").GetString().Should().Be("Original");
        var second = await PollAsync(subscription.Id, page => page.Data.Count == 1, first.Next, 1);
        second.Data[0].Revision.Should().Be(2);
        var replay = Value(await Delivery.PullAsync(subscription.Id, limit: 1));
        replay.Data[0].EventId.Should().Be(first.Data[0].EventId);
        var empty = Value(await Delivery.PullAsync(subscription.Id, second.Next));
        empty.Data.Should().BeEmpty(); empty.Next.Should().Be(second.Next);
    }

    /// <summary>Source-local links with the same declared origin resolve to one post with field provenance.</summary>
    [Fact]
    public async Task SaveCapture_ShouldShareOnePost_WhenOriginsAgree()
    {
        var context = await ContextAsync();
        var origin = "urn:event:" + Guid.NewGuid().ToString("N");
        var first = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, Capture("one") with { OriginKey = origin }, Envelope("One")));
        var second = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, Capture("two") with { OriginKey = origin }, Envelope("Two")));
        second.Post.Id.Should().Be(first.Post.Id); second.Post.Links.Should().HaveCount(2);
        second.Post.FieldSources.Should().ContainKey("title");
        second.Post.Links.Select(link => link.Id).Should().Contain(second.Post.FieldSources["title"]);
    }

    /// <summary>A merge can restore its original links without destroying either post identity.</summary>
    [Fact]
    public async Task Unmerge_ShouldRestoreLinkMembership_WhenTheMergeWasWrong()
    {
        var context = await ContextAsync();
        var first = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, Capture("first"), Envelope("First")));
        var second = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, Capture("second"), Envelope("Second")));
        var merge = Value(await Posts.MergeAsync(context.Container, first.Post.Id, second.Post.Id));
        Value(await Posts.GetPostAsync(context.Container, first.Post.Id)).Links.Should().HaveCount(2);
        Value(await Posts.UnmergeAsync(context.Container, merge.Id)).Undone.Should().BeTrue();
        Value(await Posts.GetPostAsync(context.Container, first.Post.Id)).Links.Should().ContainSingle();
        Value(await Posts.GetPostAsync(context.Container, second.Post.Id)).Links.Should().ContainSingle();
        Value(await Posts.UnmergeAsync(context.Container, merge.Id)).Undone.Should().BeTrue();
    }

    /// <summary>Direct absence needs another check, while old captured evidence cannot revive a missing post.</summary>
    [Fact]
    public async Task ObserveLink_ShouldRequireAnother404_WhenThePageDisappears()
    {
        var context = await ContextAsync();
        var capture = Capture("removed");
        Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, capture, Envelope("Removed")));
        var missing = new LinkObservation { ExternalKey = capture.ExternalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_404", HttpStatus = 404 };
        Value(await Posts.ObserveLinkAsync(context.Container, context.Source, missing)).State.Should().Be(PostState.Suspect);
        Value(await Posts.ObserveLinkAsync(context.Container, context.Source, missing)).State.Should().Be(PostState.Gone);
        Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, capture, Envelope("Removed"))).Post.State.Should().Be(PostState.Gone);
    }

    /// <summary>A subscription can replay existing live posts when its consumer joins.</summary>
    [Fact]
    public async Task Subscribe_ShouldBackfillLivePosts_WhenConsumerJoins()
    {
        var context = await ContextAsync();
        var saved = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, Capture("existing"), Envelope("Existing")));
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context)));
        var page = await PollAsync(subscription.Id, page => page.Data.Count == 1);
        page.Data[0].PostId.Should().Be(saved.Post.Id); page.Data[0].Reason.Should().Be("backfill");
        page.Data[0].Context["catalog"].Should().Be("fixture");
    }

    /// <summary>Container boundaries and opaque cursor ownership are checked by every form.</summary>
    [Fact]
    public async Task Read_ShouldRejectWrongContainerAndCursor_WhenReferencesCrossBoundaries()
    {
        var context = await ContextAsync();
        var saved = Value(await Posts.SaveCaptureAsync(context.Container, context.Source, null, Capture("private"), Envelope("Private")));
        Error(await Posts.GetPostAsync(Guid.NewGuid(), saved.Post.Id)).Type.Should().Be(AppErrorType.NotFound);
        var first = Value(await Delivery.SubscribeAsync(Declaration(context)));
        var second = Value(await Delivery.SubscribeAsync(Declaration(context)));
        var cursor = Value(await Delivery.PullAsync(first.Id)).Next;
        Error(await Delivery.PullAsync(second.Id, cursor)).Type.Should().Be(AppErrorType.Validation);
        Error(await Delivery.PullAsync(second.Id, limit: 0)).Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>An unknown configured filter cannot silently turn into an unfiltered subscription.</summary>
    [Fact]
    public async Task Subscribe_ShouldRejectUnknownFilter_WhenTheHostHasNotRegisteredIt()
    {
        var context = await ContextAsync();

        Error(await Delivery.SubscribeAsync(Declaration(context) with { FilterKey = "unregistered-" + Guid.NewGuid().ToString("N") }))
            .Type.Should().Be(AppErrorType.Validation);
    }

    private async Task<Context> ContextAsync()
    {
        var unique = Guid.NewGuid().ToString("N");
        var tenant = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "posts-" + unique, Name = "Posts conformance" }));
        var container = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = unique + ".example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));
        var pipeline = Value(await Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = container.Id, SourceId = source.Id,
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
        }));
        return new Context(tenant.Id, container.Id, source.Id, pipeline.Id);
    }

    private async Task<CursorPage<DeliveryEvent>> PollAsync(Guid subscriptionId, Func<CursorPage<DeliveryEvent>, bool> ready, string? cursor = null, int limit = 100)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = Value(await Delivery.PullAsync(subscriptionId, cursor, limit));
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(5)) return page;
            await Task.Delay(50);
        }
    }

    private Capture Capture(string key) => new()
    {
        ExternalKey = key, Url = $"https://fixture.example/{key}", RequestUrl = $"https://fixture.example/{key}", FinalUrl = $"https://fixture.example/{key}",
        CapturedAtUtc = Clock.GetUtcNow().ToUniversalTime(), Body = Encoding.UTF8.GetBytes(key), ContentProjection = key, ContentType = "text/html", HttpStatus = 200,
    };
    private static PostEnvelope Envelope(string title) => new() { Title = title, Text = title, Kind = PostKind.Event,
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" } };
    private static SubscriptionDeclaration Declaration(Context context) => new()
    {
        TenantId = context.Tenant, ContainerId = context.Container, PipelineId = context.Pipeline,
        Context = new Dictionary<string, string> { ["catalog"] = "fixture" },
    };
    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    private static AppError Error<T>(Result<T> result) where T : notnull => result is Result<T>.Failure failure ? failure.Error : throw new Xunit.Sdk.XunitException("Expected failure.");
    private sealed record Context(Guid Tenant, Guid Container, Guid Source, Guid Pipeline);
}
