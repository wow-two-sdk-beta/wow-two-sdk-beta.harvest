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

    /// <summary>Writes a post's AI answers into the implementation's store, as the tagging and extraction passes would
    /// have left them about the post's current title and text.</summary>
    /// <param name="seed">The answers.</param>
    protected abstract Task SeedAnswersAsync(ProcessedAnswerSeed seed);

    /// <summary>Pauses a source's host until a moment, as a refused request would leave it, in the implementation's store.</summary>
    /// <param name="source">The source whose host pauses.</param>
    /// <param name="until">When the pause ends.</param>
    /// <returns>Whether the form can pause a host; the default cannot, and a case that needs a pause then reads nothing.</returns>
    protected virtual Task<bool> PauseHostAsync(SourceModel source, DateTimeOffset until) => Task.FromResult(false);

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

    /// <summary>A host paused by a refusal makes its source need attention even with a run queued behind the pause, since
    /// that run waits for the pause to end.</summary>
    [Fact]
    public async Task ListOverview_ShouldNeedAttention_WhenAPausedHostHasARunQueuedBehindThePause()
    {
        var context = await ContextAsync();
        var pipeline = await PipelineAsync(context);
        var run = Value(await Runs.StartAsync(pipeline.Id));
        var until = Clock.GetUtcNow().AddHours(1);
        if (!await PauseHostAsync(context.Source, until))
            return;

        var overview = Single(await Overview.ListOverviewAsync(context.Container));

        overview.Health.PausedUntil.Should().Be(until);
        overview.LastHarvest.Should().Match<SourceHarvestModel>(last => last.RunId == run.Id && last.Status == RunStatus.Queued);
        overview.State.Should().Be(SourceHarvestState.NeedsAttention);
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
            Dates = [new PostDate { Role = DateRole.Start, Value = "2026-11-20T19:00", Precision = DatePrecision.Minute },
                new PostDate { Role = DateRole.End, Value = "2026-11-22", Precision = DatePrecision.Day }],
            PostedAt = new PostTimestamp { Value = "2026-11-12T09:30", Precision = DatePrecision.Minute },
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
            && post.StartsAt == "2026-11-20T19:00" && post.EndsAt == "2026-11-22" && post.PostedAt == "2026-11-12T09:30" && post.Url == $"https://{context.Source.Handle}/concert"
            && post.ImageUrl == $"https://{context.Source.Handle}/poster.jpg"
            && post.ImageUrls.SequenceEqual(new[] { $"https://{context.Source.Handle}/poster.jpg", $"https://{context.Source.Handle}/second.jpg" }));
        posts.Single(post => post.PostId == untitled.Post.Id).Should().Match<SourcePostModel>(post =>
            post.Title == $"https://{context.Source.Handle}/untitled" && post.Kind == PostKind.Other && post.PipelineId == null && post.StartsAt == null && post.EndsAt == null && post.PostedAt == null && post.ImageUrl == null && post.ImageUrls.Count == 0);
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

    /// <summary>Counts the full source, reports alternate facet counts and continues below the first page.</summary>
    [Fact]
    public async Task QuerySourcePosts_ShouldSearchAndCountBeforePaging()
    {
        var context = await ContextAsync();
        var oldest = Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, null,
            Capture(context, "oldest"), Envelope("Концерт Гайбуллы")));
        for (int index = 0; index < 31; index++)
        {
            var title = $"Программа {index:D2}";
            Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, null,
                Capture(context, $"programme-{index}"), Envelope(title) with { Kind = PostKind.Other }));
        }

        var first = Value(await Overview.QuerySourcePostsAsync(context.Source.Id, new()));
        first.Items.Should().HaveCount(30);
        first.Total.Should().Be(32);
        first.NextCursor.Should().NotBeNullOrEmpty();
        var next = Value(await Overview.QuerySourcePostsAsync(context.Source.Id, new() { Cursor = first.NextCursor }));
        next.Items.Should().HaveCount(2);
        first.Items.Select(post => post.PostId).Intersect(next.Items.Select(post => post.PostId)).Should().BeEmpty();
        next.NextCursor.Should().BeNull();

        var beyondFirstPage = next.Items[^1];
        var found = Value(await Overview.QuerySourcePostsAsync(context.Source.Id, new() { Search = beyondFirstPage.Title.ToUpperInvariant() }));
        found.Items.Should().ContainSingle().Which.PostId.Should().Be(beyondFirstPage.PostId);
        var events = Value(await Overview.QuerySourcePostsAsync(context.Source.Id, new()
        {
            Filters = new Dictionary<string, string> { [SourcePostFilterKeys.Kind] = "event" },
        }));
        events.Total.Should().Be(1);
        events.Items.Should().ContainSingle().Which.PostId.Should().Be(oldest.Post.Id);
        events.Facets.Single(facet => facet.Key == SourcePostFilterKeys.Kind).Options
            .Should().Contain(option => option.Value == "other" && option.Count == 31);
        Error(await Overview.QuerySourcePostsAsync(context.Source.Id, new() { Cursor = first.NextCursor, Search = "Concert" }))
            .Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>The processed read lists every post with a tags answer — with its domains, verdict and extraction
    /// summary — and filters by entity type, verdict and extraction before paging.</summary>
    [Fact]
    public async Task QuerySourceProcessed_ShouldListTaggedPostsWithTheirVerdictAndExtraction()
    {
        var context = await ContextAsync();
        var at = Clock.GetUtcNow();
        var pipeline = await PipelineAsync(context);
        var run = await CancelledRunAsync(context, pipeline);
        var runway = await SavePostAsync(context, "runway", "Tashkent Fashion Week", ClassificationVerdict.Relevant, run.Id, envelope => envelope with
        {
            PostedAt = new PostTimestamp { Value = "2026-11-12T09:30", Precision = DatePrecision.Minute },
            Language = new PostLanguage { Code = "ru" },
            Media =
            [
                new PostMedia { Kind = MediaKind.Thumbnail, Url = $"https://{context.Source.Handle}/thumb.jpg" },
                new PostMedia { Kind = MediaKind.Image, Url = $"https://{context.Source.Handle}/poster.jpg" },
                new PostMedia { Kind = MediaKind.Image, Url = $"https://{context.Source.Handle}/second.jpg" },
            ],
        });
        var flat = await SavePostAsync(context, "flat", "2 xonali kvartira ijaraga", ClassificationVerdict.Irrelevant);
        var essay = await SavePostAsync(context, "essay", "Notes on autumn fabrics", ClassificationVerdict.Irrelevant, shape: envelope => envelope with { Kind = PostKind.News });
        await SavePostAsync(context, "untagged", "Not tagged yet", null);
        await SeedAnswersAsync(new ProcessedAnswerSeed
        {
            PostId = runway, EntityType = "event", Domains = ["style-fashion/womens-fashion", "style-fashion"],
            Topics = ["fashion-week"], About = "a fashion week", TitleEn = "Tashkent Fashion Week", Confidence = 0.9m,
            Flags = [new ProcessedFlagModel("timeframe", "unsure", "upcoming")], AnsweredAtUtc = at,
            Extraction = new ProcessedExtractionSeed
            {
                Event = new ProcessedEventSummary
                {
                    Name = "Tashkent Fashion Week", OriginalName = "Ташкентская неделя моды", EditionLabel = "2026",
                    Occurrences = [new ProcessedOccurrenceModel("2026-11-21", "2026-11-23", "day", "stated")],
                    Venue = "Hilton", City = "Tashkent", Country = "UZ", Format = "runway", Access = "ticketed",
                    Prices = [new ProcessedPriceModel(150000m, "UZS", "entry")], Status = "scheduled",
                },
            },
        });
        await SeedAnswersAsync(new ProcessedAnswerSeed
        {
            PostId = flat, EntityType = "listing", Domains = ["real-estate"], Model = "openai/gpt-5-mini", PromptVersion = 4,
            AnsweredAtUtc = at,
            Extraction = new ProcessedExtractionSeed
            {
                UseCase = "extract-listing", Model = "openai/gpt-5-mini", PromptVersion = 3,
                Listing = new ProcessedListingSummary
                {
                    OfferType = "rent", Price = new ProcessedPriceModel(500m, "USD", "month"), Rooms = 2, AreaM2 = 54m,
                    District = "Chilonzor", City = "Tashkent",
                },
            },
        });
        await SeedAnswersAsync(new ProcessedAnswerSeed { PostId = essay, EntityType = "none", Timeframe = "evergreen", AnsweredAtUtc = at });

        var all = Value(await Overview.QuerySourceProcessedAsync(context.Source.Id, new()));

        all.Total.Should().Be(3);
        all.NextCursor.Should().BeNull();
        all.Items.Select(post => post.PostId).Should().BeEquivalentTo([runway, flat, essay]);
        var shown = all.Items.Single(post => post.PostId == runway);
        shown.Title.Should().Be("Tashkent Fashion Week");
        shown.Url.Should().Be($"https://{context.Source.Handle}/runway");
        shown.Classification!.Verdict.Should().Be(ClassificationVerdict.Relevant);
        shown.Should().Match<SourceProcessedPostModel>(post => post.Kind == PostKind.Event && post.State == PostState.Live
            && post.PostedAt == "2026-11-12T09:30" && post.Language == "ru" && post.PipelineId == pipeline.Id
            && post.ImageUrl == $"https://{context.Source.Handle}/poster.jpg");
        all.Items.Single(post => post.PostId == flat).Should().Match<SourceProcessedPostModel>(post => post.Kind == PostKind.Event
            && post.State == PostState.Live && post.PostedAt == null && post.Language == null && post.PipelineId == null && post.ImageUrl == null);
        all.Items.Single(post => post.PostId == essay).Kind.Should().Be(PostKind.News);
        shown.Tags.Should().Match<ProcessedTagsModel>(tags => tags.Status == "done" && tags.Current && tags.EntityType == "event"
            && tags.About == "a fashion week" && tags.TitleEn == "Tashkent Fashion Week" && tags.Confidence == 0.9m
            && tags.Timeframe == "upcoming" && tags.Topics.SequenceEqual(new[] { "fashion-week" }));
        shown.Tags.Domains.Select(domain => (domain.Key, domain.Primary)).Should()
            .Equal(("style-fashion/womens-fashion", true), ("style-fashion", false));
        shown.Tags.Domains.Should().OnlyContain(domain => domain.Label.Length > 0);
        shown.Tags.Flags.Should().ContainSingle().Which.Should().Be(new ProcessedFlagModel("timeframe", "unsure", "upcoming"));
        shown.Tags.Answer.Should().Match<ProcessedAnswerModel>(answer => answer.Provider == "anthropic"
            && answer.Model == "claude-haiku-5-5" && answer.PromptVersion == 1);
        var extraction = shown.Extraction!;
        extraction.Should().Match<ProcessedExtractionModel>(value => value.UseCase == "extract-event" && value.Status == "done"
            && value.Role == "announcement" && value.Answer.PromptVersion == 2 && value.Listing == null);
        extraction.Event.Should().BeEquivalentTo(new ProcessedEventSummary
        {
            Name = "Tashkent Fashion Week", OriginalName = "Ташкентская неделя моды", EditionLabel = "2026",
            Occurrences = [new ProcessedOccurrenceModel("2026-11-21", "2026-11-23", "day", "stated")],
            Venue = "Hilton", City = "Tashkent", Country = "UZ", Format = "runway", Access = "ticketed",
            Prices = [new ProcessedPriceModel(150000m, "UZS", "entry")], Status = "scheduled",
        });
        var listing = all.Items.Single(post => post.PostId == flat);
        listing.Tags.Answer.Should().Match<ProcessedAnswerModel>(answer => answer.Provider == "openai" && answer.Model == "gpt-5-mini");
        listing.Extraction!.Listing.Should().BeEquivalentTo(new ProcessedListingSummary
        {
            OfferType = "rent", Price = new ProcessedPriceModel(500m, "USD", "month"), Rooms = 2, AreaM2 = 54m,
            District = "Chilonzor", City = "Tashkent",
        });
        all.Items.Single(post => post.PostId == essay).Extraction.Should().BeNull();

        Ids(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { EntityType = "event" })).Should().Equal(runway);
        Ids(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Verdict = ClassificationVerdict.Irrelevant }))
            .Should().BeEquivalentTo([flat, essay]);
        Ids(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Extracted = true })).Should().BeEquivalentTo([runway, flat]);
        Ids(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Extracted = false })).Should().Equal(essay);
        Single(await Overview.ListOverviewAsync(context.Container)).ProcessedCount.Should().Be(all.Total);
    }

    /// <summary>A source's processed count is the total of its unfiltered processed list: its non-merged posts with a tags
    /// answer of any status, never another source's posts or a post still untagged.</summary>
    [Fact]
    public async Task ListOverview_ShouldCountProcessedPostsPerSource_ByTheProcessedListsRule()
    {
        var context = await ContextAsync();
        var other = await SourceAsync(context.Container, "c-" + Unique() + ".example");
        var at = Clock.GetUtcNow();
        var tagged = await SavePostAsync(context, "tagged", "Tagged exhibition", ClassificationVerdict.Relevant);
        var failed = await SavePostAsync(context, "failed", "Tags failed", null);
        await SavePostAsync(context, "untagged", "Not tagged yet", null);
        var foreign = Value(await Posts.SaveCaptureAsync(context.Container, other.Id, null, Capture(context, "foreign"), Envelope("Foreign exhibition"))).Post.Id;
        await SeedAnswersAsync(new ProcessedAnswerSeed { PostId = tagged, Domains = ["style-fashion"], AnsweredAtUtc = at });
        await SeedAnswersAsync(new ProcessedAnswerSeed { PostId = failed, Status = "failed", Error = "rate-limited", EntityType = null, AnsweredAtUtc = at });
        await SeedAnswersAsync(new ProcessedAnswerSeed { PostId = foreign, Domains = ["style-fashion"], AnsweredAtUtc = at });

        var overviews = Value(await Overview.ListOverviewAsync(context.Container));

        var own = overviews.Single(overview => overview.SourceId == context.Source.Id);
        own.PostCount.Should().Be(3);
        own.ProcessedCount.Should().Be(2);
        overviews.Single(overview => overview.SourceId == other.Id).ProcessedCount.Should().Be(1);
        foreach (var overview in overviews)
            overview.ProcessedCount.Should().Be(Value(await Overview.QuerySourceProcessedAsync(overview.SourceId, new())).Total);
        var none = await SourceAsync(context.Container, "d-" + Unique() + ".example");
        Value(await Overview.ListOverviewAsync(context.Container)).Single(overview => overview.SourceId == none.Id).ProcessedCount.Should().Be(0);
    }

    /// <summary>A cursor continues the same source and filters below the first page; the rules refuse a bad filter,
    /// size or cursor, and an unknown source is not found.</summary>
    [Fact]
    public async Task QuerySourceProcessed_ShouldPageWithItsCursor_AndRefuseABadQuery()
    {
        var context = await ContextAsync();
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var id = await SavePostAsync(context, $"page-{index}", $"Exhibition {index}", ClassificationVerdict.Relevant);
            await SeedAnswersAsync(new ProcessedAnswerSeed { PostId = id, Domains = ["style-fashion"], AnsweredAtUtc = Clock.GetUtcNow() });
            ids.Add(id);
        }

        var first = Value(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Limit = 2 }));
        first.Items.Should().HaveCount(2);
        first.Total.Should().Be(3);
        first.NextCursor.Should().NotBeNullOrEmpty();
        var next = Value(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Limit = 2, Cursor = first.NextCursor }));
        next.Items.Should().ContainSingle();
        next.NextCursor.Should().BeNull();
        first.Items.Concat(next.Items).Select(post => post.PostId).Should().BeEquivalentTo(ids);

        Error(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Cursor = first.NextCursor, EntityType = "event" }))
            .Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Cursor = "not-a-cursor" })).Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Limit = 0 })).Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { Limit = SourceProcessedQuery.MaxLimit + 1 }))
            .Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.QuerySourceProcessedAsync(context.Source.Id, new() { EntityType = "article" })).Type.Should().Be(AppErrorType.Validation);
        Error(await Overview.QuerySourceProcessedAsync(Guid.NewGuid(), new())).Type.Should().Be(AppErrorType.NotFound);
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

    /// <summary>Stage schedules and the source master pause produce the same overview in every form.</summary>
    [Fact]
    public async Task ListOverview_ShouldRepresentStageSchedulesAndMasterPause()
    {
        var context = await ContextAsync();
        var pipeline = Value(await Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = context.Container, SourceId = context.Source.Id,
            Schedules = [new() { Kind = WorkKind.Harvest, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1), Stage = new() { BatchSize = 50 } }],
        }));
        var enabled = Single(await Overview.ListOverviewAsync(context.Container));
        enabled.SourceState.Should().Be(SourceState.Active);
        enabled.Permission.Should().Be(context.Source.Permission);
        enabled.State.Should().Be(SourceHarvestState.Healthy);
        enabled.Sections.Single().Should().Match<SourceSectionModel>(section => section.Harvesting && section.HarvestKind == WorkKind.Harvest);

        var run = Value(await Runs.StartAsync(pipeline.Id, WorkKind.Process));
        var after = Single(await Overview.ListOverviewAsync(context.Container));
        after.LastHarvest.Should().Match<SourceHarvestModel>(last => last.RunId == run.Id && last.Kind == WorkKind.Process);

        Value(await Registry.UpdateSourceAsync(context.Source.Id, new SourceUpdate { Paused = true }));
        var paused = Single(await Overview.ListOverviewAsync(context.Container));
        paused.SourceState.Should().Be(SourceState.Paused);
        paused.State.Should().Be(SourceHarvestState.Idle);
        paused.NextHarvestAt.Should().BeNull();
        paused.Sections.Single().Should().Match<SourceSectionModel>(section => !section.Harvesting && !section.Paused && section.HarvestKind == null);
        Value(await Registry.UpdateSourceAsync(context.Source.Id, new SourceUpdate { Paused = false }));
        Single(await Overview.ListOverviewAsync(context.Container)).Sections.Single().Harvesting.Should().BeTrue();
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

    private async Task<Guid> SavePostAsync(Context context, string key, string title, ClassificationVerdict? verdict,
        Guid? runId = null, Func<PostEnvelope, PostEnvelope>? shape = null)
    {
        var envelope = Envelope(title) with
        {
            Classification = verdict is { } value
                ? new PostClassification { Classifier = "model:anthropic/claude-haiku-5-5@post-tags:1", Verdict = value, Confidence = 0.9 }
                : null,
        };
        return Value(await Posts.SaveCaptureAsync(context.Container, context.Source.Id, runId, Capture(context, key), shape?.Invoke(envelope) ?? envelope)).Post.Id;
    }

    private static IEnumerable<Guid> Ids(Result<SourceProcessedPage> result) => Value(result).Items.Select(post => post.PostId);

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private static SourceOverviewModel Single(Result<IReadOnlyList<SourceOverviewModel>> result) => Value(result).Should().ContainSingle().Subject;

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private static AppError Error<T>(Result<T> result) where T : notnull => result is Result<T>.Failure failure ? failure.Error
        : throw new Xunit.Sdk.XunitException("Expected a failed result.");

    private sealed record Context(Guid Tenant, Guid Container, SourceModel Source);
}
