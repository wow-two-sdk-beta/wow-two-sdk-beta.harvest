using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Unit.Delivery;

public sealed class FakeDeliveryFilterTests
{
    private const string FilterKey = "test-title-v1";

    [Fact]
    public async Task Capture_ShouldWithholdNonmatchingDeliveryAndKeepPosts_WhenAFilterIsSelected()
    {
        var host = await CreateAsync(new TitleFilter());
        var subscription = Value(await host.Delivery.SubscribeAsync(Declaration(host)));
        var concert = Value(await SaveAsync(host, "concert", "Concert"));
        var fashion = Value(await SaveAsync(host, "fashion", "Fashion market"));

        var page = Value(await host.Delivery.PullAsync(subscription.Id));

        page.Data.Should().ContainSingle(); page.Data[0].PostId.Should().Be(fashion.Post.Id);
        page.Data[0].Context["catalog"].Should().Be("opaque-routing-data");
        Value(await host.Posts.GetPostAsync(host.Container, concert.Post.Id)).Base.Title.Should().Be("Concert");
        Value(await host.Delivery.GetSubscriptionAsync(subscription.Id)).FilterKey.Should().Be(FilterKey);
    }

    [Fact]
    public async Task Subscribe_ShouldFilterBackfillAndReplayStableEvents_WhenPostsAlreadyExist()
    {
        var host = await CreateAsync(new TitleFilter());
        Value(await SaveAsync(host, "concert", "Concert"));
        var capture = Capture(host, "fashion", "Fashion market");
        var saved = Value(await host.Posts.SaveCaptureAsync(host.Container, host.Source, null, capture, Envelope("Fashion market")));
        var subscription = Value(await host.Delivery.SubscribeAsync(Declaration(host)));
        var repeated = Value(await host.Posts.SaveCaptureAsync(host.Container, host.Source, null, capture, Envelope("Fashion market")));

        var page = Value(await host.Delivery.PullAsync(subscription.Id));

        repeated.Changed.Should().BeFalse();
        page.Data.Should().ContainSingle(); page.Data[0].PostId.Should().Be(saved.Post.Id); page.Data[0].Reason.Should().Be("backfill");
        Value(await host.Delivery.PullAsync(subscription.Id)).Data[0].EventId.Should().Be(page.Data[0].EventId);
        var empty = Value(await host.Delivery.PullAsync(subscription.Id, page.Next));
        empty.Data.Should().BeEmpty(); empty.Next.Should().Be(page.Next);
    }

    [Fact]
    public async Task Subscribe_ShouldRejectMissingFilterAndPreserveUnfilteredBehavior_WhenNoFiltersAreRegistered()
    {
        var host = await CreateAsync();
        (await host.Delivery.SubscribeAsync(Declaration(host))).Should().BeOfType<Result<SubscriptionModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Validation);
        var subscription = Value(await host.Delivery.SubscribeAsync(Declaration(host) with { FilterKey = null }));

        var saved = Value(await SaveAsync(host, "concert", "Concert"));

        Value(await host.Delivery.PullAsync(subscription.Id)).Data.Should().ContainSingle().Which.PostId.Should().Be(saved.Post.Id);
    }

    [Fact]
    public async Task Capture_ShouldPreserveGoneRestoreAndMerge_WhenAKnownPostStopsMatching()
    {
        var host = await CreateAsync(new TitleFilter());
        var subscription = Value(await host.Delivery.SubscribeAsync(Declaration(host)));
        var capture = Capture(host, "known", "Fashion market");
        var known = Value(await host.Posts.SaveCaptureAsync(host.Container, host.Source, null, capture, Envelope("Fashion market")));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var changed = capture with { ContentProjection = "Concert instead", CapturedAtUtc = host.Clock.GetUtcNow() };
        Value(await host.Posts.SaveCaptureAsync(host.Container, host.Source, null, changed, Envelope("Concert")));
        Value(await host.Posts.ObserveLinkAsync(host.Container, host.Source,
            new LinkObservation { ExternalKey = capture.ExternalKey, Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410 }));
        host.Clock.Advance(TimeSpan.FromHours(1));
        Value(await host.Posts.SaveCaptureAsync(host.Container, host.Source, null, changed with { CapturedAtUtc = host.Clock.GetUtcNow() }, Envelope("Concert")));
        var survivor = Value(await SaveAsync(host, "survivor", "Another concert"));
        Value(await host.Posts.MergeAsync(host.Container, survivor.Post.Id, known.Post.Id));

        var page = Value(await host.Delivery.PullAsync(subscription.Id));

        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.LinkState,
            DeliveryEventTypes.PostChanged, DeliveryEventTypes.PostMerged);
        page.Data.Should().OnlyContain(item => item.PostId == known.Post.Id);
        page.Data[1].Data!.Value.GetProperty("state").GetString().Should().Be("gone");
        page.Data[2].Data!.Value.GetProperty("state").GetString().Should().Be("live");
        page.Data[3].Data!.Value.GetProperty("mergedInto").GetGuid().Should().Be(survivor.Post.Id);
    }

    [Fact]
    public async Task Capture_ShouldKeepPostAndWithholdDelivery_WhenTheFilterThrows()
    {
        var host = await CreateAsync(new ThrowingFilter());
        var subscription = Value(await host.Delivery.SubscribeAsync(Declaration(host)));

        var saved = Value(await SaveAsync(host, "fashion", "Fashion market"));

        Value(await host.Delivery.PullAsync(subscription.Id)).Data.Should().BeEmpty();
        Value(await host.Posts.GetPostAsync(host.Container, saved.Post.Id)).Base.Title.Should().Be("Fashion market");
    }

    [Fact]
    public async Task Pull_ShouldPreserveMappedContentLinks_WhenProjectingTheEventSchema()
    {
        var host = await CreateAsync();
        var subscription = Value(await host.Delivery.SubscribeAsync(Declaration(host) with { FilterKey = null, Schema = "wow2.event.v1" }));
        var capture = Capture(host, "event", "Event description");
        var envelope = Envelope("Event") with
        {
            Extras = new Dictionary<string, JsonElement>
            {
                ["event"] = JsonSerializer.SerializeToElement(new
                {
                    schema = "wow2.event.v1", title = "Event",
                    links = new[] { new { url = "https://organizer.example/register", rel = "registration" } },
                }),
            },
        };
        Value(await host.Posts.SaveCaptureAsync(host.Container, host.Source, null, capture, envelope));

        var payload = Value(await host.Delivery.PullAsync(subscription.Id)).Data.Single().Data!.Value;

        payload.GetProperty("links")[0].GetProperty("url").GetString().Should().Be(capture.Url);
        payload.GetProperty("contentLinks")[0].GetProperty("url").GetString().Should().Be("https://organizer.example/register");
        payload.GetProperty("contentLinks")[0].GetProperty("rel").GetString().Should().Be("registration");
    }

    private static async Task<Context> CreateAsync(params IDeliveryPostFilter[] filters)
    {
        var clock = new Clock();
        var registry = new FakeRegistrationBroker(clock);
        var tenant = Value(await registry.RegisterTenantAsync(new TenantRegistration { Slug = "events", Name = "Events" }));
        var container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool }));
        var source = Value(await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "fixture.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));
        var pipeline = Value(await registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = container.Id, SourceId = source.Id,
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
        }));
        var posts = new FakePostBroker(registry, clock);
        return new Context(tenant.Id, container.Id, source.Id, pipeline.Id, clock, posts, new FakeDeliveryBroker(posts, registry, clock, filters));
    }

    private static Task<Result<CaptureSaveResult>> SaveAsync(Context host, string key, string title) =>
        host.Posts.SaveCaptureAsync(host.Container, host.Source, null, Capture(host, key, title), Envelope(title));

    private static Capture Capture(Context host, string key, string text) => new()
    {
        ExternalKey = key, Url = $"https://fixture.example/{key}", RequestUrl = $"https://fixture.example/{key}", FinalUrl = $"https://fixture.example/{key}",
        CapturedAtUtc = host.Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, HttpStatus = 200, ContentType = "text/html",
    };
    private static PostEnvelope Envelope(string title) => new()
    {
        Kind = PostKind.Event, Title = title, Text = title, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };
    private static SubscriptionDeclaration Declaration(Context host) => new()
    {
        TenantId = host.Tenant, ContainerId = host.Container, PipelineId = host.Pipeline, FilterKey = FilterKey,
        Context = new Dictionary<string, string> { ["catalog"] = "opaque-routing-data" },
    };
    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    private sealed record Context(Guid Tenant, Guid Container, Guid Source, Guid Pipeline, Clock Clock, FakePostBroker Posts, FakeDeliveryBroker Delivery);
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan amount) => now += amount;
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
