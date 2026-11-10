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
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Sources;

/// <summary>Specifies the console's Sources reads shared by the module, the HTTP form and the consumer fakes.</summary>
/// <remarks>No worker runs: a started run stays queued until a case cancels it, and captures are saved directly.</remarks>
public abstract class SourceOverviewBrokerConformance
{
    /// <summary>Gets the registry that creates each case's isolated tenant and container.</summary>
    protected abstract IRegistrationBroker Registry { get; }
    /// <summary>Gets the run implementation; no worker may execute queued work.</summary>
    protected abstract IRunBroker Runs { get; }
    /// <summary>Gets the post implementation that stores the cases' captures.</summary>
    protected abstract IPostBroker Posts { get; }
    /// <summary>Gets the delivery implementation that holds the cases' subscriptions.</summary>
    protected abstract IDeliveryBroker Delivery { get; }
    /// <summary>Gets the overview implementation under test.</summary>
    protected abstract ISourceOverviewBroker Overview { get; }
    /// <summary>Gets the implementation's clock.</summary>
    protected abstract TimeProvider Clock { get; }

    /// <summary>Lets a form whose post store checks run references learn a run the case started.</summary>
    /// <param name="run">The started run.</param>
    /// <param name="sourceId">The run's source.</param>
    protected virtual void OnRunStarted(RunModel run, Guid sourceId) { }

    /// <summary>Every registered source has one overview, named by its display name or else its handle.</summary>
    [Fact]
    public async Task ListOverview_ShouldListEverySourceOnce_WhenNothingHarvests()
    {
        var context = await ContextAsync();
        var second = await SourceAsync(context.Container, "b-" + Unique() + ".example");
        var group = Value(await Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = context.Container, Name = "Publisher" }));
        Value(await Registry.UpdateSourceAsync(second.Id, new SourceUpdate { Name = " A Second ", GroupId = group.Id }));

        var overviews = Value(await Overview.ListOverviewAsync(context.Container));

        overviews.Select(overview => overview.SourceId).Should().Equal(second.Id, context.Source.Id);
        overviews[0].Should().Match<SourceOverviewModel>(overview => overview.Name == "A Second" && overview.GroupId == group.Id
            && overview.Handle == second.Handle && overview.Family == SourceFamily.Site);
        var first = overviews[1];
        first.Name.Should().Be(context.Source.Handle);
        first.GroupId.Should().BeNull();
        first.State.Should().Be(SourceHarvestState.Idle);
        first.LastHarvest.Should().BeNull();
        first.NextHarvestAt.Should().BeNull();
        first.Sections.Should().BeEmpty();
        first.NewPostsDaily.Should().HaveCount(SourceConstants.OverviewDays).And.OnlyContain(count => count == 0);
        first.Health.Should().Match<SourceHealthModel>(health => health.HostKey == context.Source.Handle
            && health.MinDelayMs == SourceConstants.DefaultReadingDelayMs && health.PausedUntil == null && health.PauseReason == null
            && health.ConsecutiveFailures == 0 && health.LastRefusal == null);
        Error(await Overview.ListOverviewAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>A section with an enabled capture schedule harvests; a queued run makes the source harvesting, and a
    /// paused pipeline stops its section.</summary>
    [Fact]
    public async Task ListOverview_ShouldFollowTheSectionsAndRuns_WhenAPipelineHarvests()
    {
        var context = await ContextAsync();
        var pipeline = await PipelineAsync(context);

        var healthy = Single(await Overview.ListOverviewAsync(context.Container));
        healthy.State.Should().Be(SourceHarvestState.Healthy);
        var section = healthy.Sections.Should().ContainSingle().Subject;
        section.Should().Match<SourceSectionModel>(value => value.PipelineId == pipeline.Id && value.Name == "Afisha · Ocharo"
            && value.Url == $"https://{context.Source.Handle}/ru/fashion" && value.Harvesting && !value.Paused
            && value.CaptureEvery == TimeSpan.FromHours(6) && value.CheckEvery == TimeSpan.FromDays(1) && value.MaxPages == 5
            && value.LastRun == null && value.Posts == 0 && value.Subscribers == 0);

        var run = Value(await Runs.StartAsync(pipeline.Id));
        var harvesting = Single(await Overview.ListOverviewAsync(context.Container));
        harvesting.State.Should().Be(SourceHarvestState.Harvesting);
        harvesting.LastHarvest.Should().Match<SourceHarvestModel>(last => last.RunId == run.Id && last.PipelineId == pipeline.Id
            && last.Status == RunStatus.Queued && last.NewPosts == 0 && last.Found == 0);
        harvesting.Sections[0].LastRun.Should().Match<SourceSectionRunModel>(last => last.RunId == run.Id && last.Kind == WorkKind.Capture);

        Value(await Runs.CancelAsync(run.Id));
        var cancelled = Single(await Overview.ListOverviewAsync(context.Container));
        cancelled.State.Should().Be(SourceHarvestState.Healthy);
        cancelled.LastHarvest!.Status.Should().Be(RunStatus.Cancelled);
        cancelled.LastHarvest.Reason.Should().Be("cancelled");

        Value(await Runs.SetPausedAsync(pipeline.Id, true));
        var paused = Single(await Overview.ListOverviewAsync(context.Container));
        paused.State.Should().Be(SourceHarvestState.Idle);
        paused.Sections[0].Should().Match<SourceSectionModel>(value => value.Paused && !value.Harvesting && value.NextDueAt == null);
    }

    /// <summary>Today's new links, the posts a section first captured and its active subscribers are counted.</summary>
    [Fact]
    public async Task ListOverview_ShouldCountNewPostsAndSubscribers_WhenASectionCaptured()
    {
        var context = await ContextAsync();
        var pipeline = await PipelineAsync(context);
        var run = await CancelledRunAsync(context, pipeline);
        Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, run.Id, Capture(context, "one"), Envelope("One")));
        Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, null, Capture(context, "two"), Envelope("Two")));
        Value(await Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = context.Tenant, ContainerId = context.Container, PipelineId = pipeline.Id, Backfill = BackfillMode.None,
        }));

        var overview = Single(await Overview.ListOverviewAsync(context.Container));

        overview.NewPostsDaily[^1].Should().Be(2);
        overview.NewPostsDaily.Sum().Should().Be(2);
        overview.Sections[0].Posts.Should().Be(1);
        overview.Sections[0].Subscribers.Should().Be(1);
    }

    /// <summary>A source's posts list newest first with the section whose run first captured each, its stated start
    /// and its first image's source URL.</summary>
    [Fact]
    public async Task ListSourcePosts_ShouldDescribeEachPost_WhenTheSourceCapturedThem()
    {
        var context = await ContextAsync();
        var pipeline = await PipelineAsync(context);
        var run = await CancelledRunAsync(context, pipeline);
        var withRun = Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, run.Id, Capture(context, "concert"), Envelope("Concert") with
        {
            Dates = [new PostDate { Role = DateRole.Start, Value = "2026-11-20T19:00", Precision = DatePrecision.Minute }],
            Media =
            [
                new PostMedia { Kind = MediaKind.Thumbnail, Url = $"https://{context.Source.Handle}/thumb.jpg" },
                new PostMedia { Kind = MediaKind.Image, Url = $"https://{context.Source.Handle}/poster.jpg", FileRef = "files://stored/poster" },
                new PostMedia { Kind = MediaKind.Image, Url = $"https://{context.Source.Handle}/second.jpg" },
            ],
        }));
        var untitled = Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, null, Capture(context, "untitled"),
            Envelope("ignored") with { Title = null, Kind = null }));

        var posts = Value(await Overview.ListSourcePostsAsync(context.Source.Id));

        posts.Should().HaveCount(2).And.BeInDescendingOrder(post => post.FirstSeenAt);
        posts.Single(post => post.PostId == withRun.Post.Id).Should().Match<SourcePostModel>(post => post.Title == "Concert"
            && post.Kind == PostKind.Event && post.State == PostState.Live && post.Revision == 1 && post.PipelineId == pipeline.Id
            && post.StartsAt == "2026-11-20T19:00" && post.Url == $"https://{context.Source.Handle}/concert"
            && post.ImageUrl == $"https://{context.Source.Handle}/poster.jpg");
        posts.Single(post => post.PostId == untitled.Post.Id).Should().Match<SourcePostModel>(post =>
            post.Title == $"https://{context.Source.Handle}/untitled" && post.Kind == PostKind.Other && post.PipelineId == null && post.StartsAt == null && post.ImageUrl == null);
        Value(await Overview.ListSourcePostsAsync(context.Source.Id, limit: 1)).Should().ContainSingle();
    }

    /// <summary>The list's limit and source are checked.</summary>
    [Fact]
    public async Task ListSourcePosts_ShouldRejectABadLimitOrAnUnknownSource()
    {
        var context = await ContextAsync();

        Error(await Overview.ListSourcePostsAsync(context.Source.Id, limit: 0)).Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.ListSourcePostsAsync(context.Source.Id, limit: SourceConstants.MaxPostsLimit + 1)).Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.ListSourcePostsAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>A post's creation and each rise of its revision are recorded with the base fields that changed.</summary>
    [Fact]
    public async Task ListPostRevisions_ShouldRecordCreationAndChangedFields_WhenThePostChanges()
    {
        var context = await ContextAsync();
        var capture = Capture(context, "revised");
        var created = Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, null, capture, Envelope("Original") with { Text = "Same text" }));
        Value(await Overview.ListPostRevisionsAsync(context.Container, created.Post.Id)).Should().ContainSingle()
            .Which.Should().Match<PostRevisionModel>(revision => revision.Revision == 1 && revision.Changed.SequenceEqual(new[] { PostRevisionConstants.Created }));

        Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, null,
            capture with { ContentProjection = "changed", CapturedAtUtc = capture.CapturedAtUtc.AddSeconds(1) },
            Envelope("Changed") with { Text = "Same text" }));
        var revisions = Value(await Overview.ListPostRevisionsAsync(context.Container, created.Post.Id));

        revisions.Select(revision => revision.Revision).Should().Equal(2, 1);
        revisions[0].Changed.Should().Contain("title").And.NotContain("text").And.NotContain("extraction").And.NotContain(PostRevisionConstants.Created);
        Error(await Overview.ListPostRevisionsAsync(Guid.NewGuid(), created.Post.Id)).Type.Should().Be(AppErrorType.NotFound);
        Error(await Overview.ListPostRevisionsAsync(context.Container, Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    private async Task<Context> ContextAsync()
    {
        var tenant = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "sources-" + Unique(), Name = "Sources conformance" }));
        var container = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        return new Context(tenant.Id, container.Id, await SourceAsync(container.Id, "a-" + Unique() + ".example"));
    }

    private async Task<SourceModel> SourceAsync(Guid container, string handle) => Value(await Registry.RegisterSourceAsync(new SourceRegistration
    {
        ContainerId = container, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
        Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
    }));

    private async Task<PipelineModel> PipelineAsync(Context context) => Value(await Registry.DeclarePipelineAsync(new PipelineDeclaration
    {
        ContainerId = context.Container, SourceId = context.Source.Id, Name = "Afisha · Ocharo",
        Scope = new PipelineScope { Url = $"https://{context.Source.Handle}/ru/fashion" },
        Limits = new PipelineLimits { MaxPages = 5 },
        Schedules =
        [
            new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6) },
            new ScheduleDeclaration { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = TimeSpan.FromDays(1) },
        ],
    }));

    private async Task<RunModel> CancelledRunAsync(Context context, PipelineModel pipeline)
    {
        var run = Value(await Runs.StartAsync(pipeline.Id));
        OnRunStarted(run, context.Source.Id);
        return Value(await Runs.CancelAsync(run.Id));
    }

    private Capture Capture(Context context, string key) => new()
    {
        ExternalKey = key, Url = $"https://{context.Source.Handle}/{key}", RequestUrl = $"https://{context.Source.Handle}/{key}",
        FinalUrl = $"https://{context.Source.Handle}/{key}", CapturedAtUtc = Clock.GetUtcNow().ToUniversalTime(),
        Body = Encoding.UTF8.GetBytes(key), ContentProjection = key, ContentType = "text/html", HttpStatus = 200,
    };

    private static PostEnvelope Envelope(string title) => new()
    {
        Title = title, Text = title, Kind = PostKind.Event,
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private static SourceOverviewModel Single(Result<IReadOnlyList<SourceOverviewModel>> result) => Value(result).Should().ContainSingle().Subject;

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private static AppError Error<T>(Result<T> result) where T : notnull => result is Result<T>.Failure failure ? failure.Error
        : throw new Xunit.Sdk.XunitException("Expected a failed result.");

    private sealed record Context(Guid Tenant, Guid Container, SourceModel Source);
}
