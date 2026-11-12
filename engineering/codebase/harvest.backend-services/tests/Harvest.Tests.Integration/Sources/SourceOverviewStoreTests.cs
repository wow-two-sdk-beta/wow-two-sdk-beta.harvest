using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Sources;

/// <summary>The Sources reads over PostgreSQL: one source with two sections, a partial capture run, a refused check run,
/// posts first seen on two days, a subscriber and a paused host lane — every overview field asserted.</summary>
public sealed class SourceOverviewStoreTests
{
    private const string Handle = "www.afisha.uz";
    private static readonly DateTimeOffset DayOne = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListOverview_ShouldDescribeEveryField_WhenTheSourceHasHarvested()
    {
        await using var host = await SourceHost.StartAsync(DayOne);
        var source = await RegisterAsync(host);
        var group = SourceHost.Value(await host.Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = host.ContainerId, Name = "Afisha" }));
        SourceHost.Value(await host.Registry.UpdateSourceAsync(source, new SourceUpdate { Name = "Afisha.uz", GroupId = group.Id }));
        var fashion = await FashionAsync(host, source);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var theatre = SourceHost.Value(await host.Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = host.ContainerId, SourceId = source, Name = "Afisha · Theatre", Scope = new PipelineScope { Url = $"https://{Handle}/ru/theatre" },
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = TimeSpan.FromDays(1) }],
        }));

        // Day one: a capture run that stopped at its page limit captures two posts.
        var capture = await RunAsync(host, fashion.Id, "capture", "partial", "page_limit", 4, 23, DayOne, DayOne.AddMinutes(5));
        host.Clock.Advance(TimeSpan.FromMinutes(3));
        SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, capture, host.Capture(Handle, "concert", "A concert"),
            SourceHost.Envelope("Concert", "A concert")));
        SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, capture, host.Capture(Handle, "expo", "An expo"),
            SourceHost.Envelope("Expo", "An expo")));

        // Day two: a post with no run, a check run refused by a challenge, a subscriber, the operator's pace and a paused lane.
        host.Clock.Advance(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero) - host.Clock.GetUtcNow());
        var now = host.Clock.GetUtcNow();
        SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, null, host.Capture(Handle, "lecture", "A lecture"),
            SourceHost.Envelope("Lecture", "A lecture")));
        var check = await RunAsync(host, theatre.Id, "check", "partial", "source_outcome", 0, 0, now.AddMinutes(-50), now.AddMinutes(-49),
            "challenge", "captcha wall");
        SourceHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = fashion.Id, Backfill = BackfillMode.None,
        }));
        var due = now.AddHours(2);
        await host.ExecuteAsync("UPDATE harvest_schedules SET next_due_at=@due WHERE pipeline_id=@pipeline", ("due", due), ("pipeline", fashion.Id));
        await host.ExecuteAsync("UPDATE harvest_schedules SET next_due_at=@due WHERE pipeline_id=@pipeline", ("due", now.AddHours(1)), ("pipeline", theatre.Id));
        SourceHost.Value(await host.Runs.SetReadingPaceAsync(source, 5000));
        await host.ExecuteAsync("""
            INSERT INTO harvest_host_lane(host_key,lane,consecutive_failures,last_challenge_kind,paused_until)
            VALUES(@host,'default',3,'captcha',@until)
            """, ("host", Handle), ("until", now.AddMinutes(30)));

        var overview = SourceHost.Value(await host.Overview.ListOverviewAsync(host.ContainerId)).Should().ContainSingle().Subject;

        overview.Should().Match<SourceOverviewModel>(value => value.SourceId == source && value.Name == "Afisha.uz" && value.Family == SourceFamily.Site
            && value.Handle == Handle && value.GroupId == group.Id && value.State == SourceHarvestState.NeedsAttention && value.NextHarvestAt == due);
        overview.LastHarvest.Should().BeEquivalentTo(new SourceHarvestModel
        {
            RunId = capture, PipelineId = fashion.Id, At = DayOne.AddMinutes(5), Status = RunStatus.Partial, Reason = "page_limit", NewPosts = 4, Found = 23,
        });
        overview.NewPostsDaily.Should().Equal(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 1);
        overview.Sections.Select(section => section.PipelineId).Should().Equal(fashion.Id, theatre.Id);
        overview.Sections[0].Should().BeEquivalentTo(new SourceSectionModel
        {
            PipelineId = fashion.Id, Name = "Afisha · Ocharo", Url = $"https://{Handle}/ru/fashion", Harvesting = true, Paused = false,
            CaptureEvery = TimeSpan.FromHours(6), CheckEvery = TimeSpan.FromDays(1), MaxPages = 5,
            LastRun = new SourceSectionRunModel
            {
                RunId = capture, Kind = WorkKind.Capture, At = DayOne.AddMinutes(5), Status = RunStatus.Partial, Reason = "page_limit", NewPosts = 4, Found = 23,
            },
            NextDueAt = due, Posts = 2, Subscribers = 1,
        });
        overview.Sections[1].Should().BeEquivalentTo(new SourceSectionModel
        {
            PipelineId = theatre.Id, Name = "Afisha · Theatre", Url = $"https://{Handle}/ru/theatre", Harvesting = false, Paused = false,
            CaptureEvery = null, CheckEvery = TimeSpan.FromDays(1), MaxPages = null,
            LastRun = new SourceSectionRunModel
            {
                RunId = check, Kind = WorkKind.Check, At = now.AddMinutes(-49), Status = RunStatus.Partial, Reason = "source_outcome", NewPosts = 0, Found = 0,
            },
            NextDueAt = null, Posts = 0, Subscribers = 0,
        });
        overview.Health.Should().BeEquivalentTo(new SourceHealthModel
        {
            HostKey = Handle, MinDelayMs = 5000, PausedUntil = now.AddMinutes(30), PauseReason = "captcha", ConsecutiveFailures = 3,
            LastRefusal = new SourceRefusalModel { At = now.AddMinutes(-49), Outcome = FetchOutcome.Challenge, Message = "captcha wall" },
        });

        await host.ExecuteAsync("UPDATE harvest_host_lane SET paused_until=NULL,consecutive_failures=0 WHERE host_key=@host", ("host", Handle));
        SourceHost.Value(await host.Overview.ListOverviewAsync(host.ContainerId))[0].State.Should().Be(SourceHarvestState.Healthy);
        await RunAsync(host, theatre.Id, "check", "queued", null, 0, 0, now, null);
        SourceHost.Value(await host.Overview.ListOverviewAsync(host.ContainerId))[0].State.Should().Be(SourceHarvestState.Harvesting);
    }

    [Fact]
    public async Task ListSourcePosts_ShouldListNewestFirst_WithTheSectionThatFirstCapturedEach()
    {
        await using var host = await SourceHost.StartAsync(DayOne);
        var source = await RegisterAsync(host);
        var fashion = await FashionAsync(host, source);
        var run = await RunAsync(host, fashion.Id, "capture", "completed", "exhausted", 1, 1, DayOne, DayOne.AddMinutes(1));
        var concert = SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, run, host.Capture(Handle, "concert", "A concert"),
            SourceHost.Envelope("Concert", "A concert") with
            {
                Dates = [new PostDate { Role = DateRole.End, Value = "2026-11-21", Precision = DatePrecision.Day },
                    new PostDate { Role = DateRole.Start, Value = "2026-11-20T19:00", Precision = DatePrecision.Minute }],
                PostedAt = new PostTimestamp { Value = "2026-11-12", Precision = DatePrecision.Day },
                Media = [new PostMedia { Kind = MediaKind.Video, Url = $"https://{Handle}/clip.mp4" },
                    new PostMedia { Kind = MediaKind.Image, Url = $"https://{Handle}/poster.jpg", FileRef = "files://stored/poster" }],
            }));
        host.Clock.Advance(TimeSpan.FromDays(1));
        var lecture = SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, null, host.Capture(Handle, "lecture", "A lecture"),
            SourceHost.Envelope("Lecture", "A lecture")));
        var elsewhere = await RegisterAsync(host, "voyage.uz");
        SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, elsewhere, null, host.Capture("voyage.uz", "trip", "A trip"),
            SourceHost.Envelope("Trip", "A trip")));

        var posts = SourceHost.Value(await host.Overview.ListSourcePostsAsync(source));

        posts.Select(post => post.PostId).Should().Equal(lecture.Post.Id, concert.Post.Id);
        posts[0].Should().BeEquivalentTo(new SourcePostModel
        {
            PostId = lecture.Post.Id, Title = "Lecture", Kind = PostKind.Event, State = PostState.Live, Revision = 1,
            FirstSeenAt = DayOne.AddDays(1), LastChangedAt = DayOne.AddDays(1), Url = $"https://{Handle}/lecture",
        });
        posts[1].Should().BeEquivalentTo(new SourcePostModel
        {
            PostId = concert.Post.Id, Title = "Concert", Kind = PostKind.Event, State = PostState.Live, Revision = 1,
            FirstSeenAt = DayOne, LastChangedAt = DayOne, PipelineId = fashion.Id, StartsAt = "2026-11-20T19:00", EndsAt = "2026-11-21", PostedAt = "2026-11-12", Url = $"https://{Handle}/concert",
            ImageUrl = $"https://{Handle}/poster.jpg", ImageUrls = [$"https://{Handle}/poster.jpg"],
        });
        SourceHost.Value(await host.Overview.ListSourcePostsAsync(source, 1)).Should().ContainSingle().Which.PostId.Should().Be(lecture.Post.Id);
    }

    [Fact]
    public async Task PostResolve_ShouldWriteRevisions_OnCreateChangeStateAndMerge()
    {
        await using var host = await SourceHost.StartAsync(DayOne);
        var source = await RegisterAsync(host);
        var capture = host.Capture(Handle, "concert", "A concert");
        var created = SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, null, capture, SourceHost.Envelope("Concert", "A concert")));
        var post = created.Post.Id;

        (await Revisions(host, post)).Should().ContainSingle().Which.Should().BeEquivalentTo(new PostRevisionModel
        {
            Revision = 1, At = DayOne, Changed = [PostRevisionConstants.Created],
        });

        host.Clock.Advance(TimeSpan.FromHours(1));
        SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, null,
            capture with { ContentProjection = "A concert, moved", CapturedAtUtc = host.Clock.GetUtcNow() },
            SourceHost.Envelope("Concert moved", "A concert") with
            {
                Dates = [new PostDate { Role = DateRole.Start, Value = "2026-11-21", Precision = DatePrecision.Day }],
                Extraction = new ExtractionStamp { NormVersion = "test@2", RulesVersion = "test@2" },
            }));
        var changed = await Revisions(host, post);
        changed.Select(revision => revision.Revision).Should().Equal(2, 1);
        changed[0].Should().BeEquivalentTo(new PostRevisionModel { Revision = 2, At = DayOne.AddHours(1), Changed = ["dates", "title"] });

        host.Clock.Advance(TimeSpan.FromHours(1));
        SourceHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, source, new LinkObservation
        {
            ExternalKey = "concert", Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410,
        })).State.Should().Be(PostState.Gone);
        (await Revisions(host, post))[0].Should().BeEquivalentTo(new PostRevisionModel { Revision = 3, At = DayOne.AddHours(2), Changed = [] },
            "a state change raises the revision with no base field changed");

        var other = SourceHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, source, null, host.Capture(Handle, "other", "Another"),
            SourceHost.Envelope("Other", "Another"))).Post.Id;
        SourceHost.Value(await host.Posts.MergeAsync(host.ContainerId, other, post));
        (await Revisions(host, post)).Select(revision => revision.Revision).Should().Equal(4, 3, 2, 1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_revisions WHERE post_id=@post", ("post", post))).Should().Be(4);

        await host.ExecuteAsync("DELETE FROM harvest_post_revisions WHERE post_id=@post", ("post", other));
        (await Revisions(host, other)).Should().BeEmpty("a post from before the revision log has no rows");
    }

    private static async Task<IReadOnlyList<PostRevisionModel>> Revisions(SourceHost host, Guid post) =>
        SourceHost.Value(await host.Overview.ListPostRevisionsAsync(host.ContainerId, post));

    private static async Task<Guid> RegisterAsync(SourceHost host, string handle = Handle) =>
        SourceHost.Value(await host.Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = host.ContainerId, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).Id;

    private static async Task<PipelineModel> FashionAsync(SourceHost host, Guid source) =>
        SourceHost.Value(await host.Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = host.ContainerId, SourceId = source, Name = "Afisha · Ocharo", Scope = new PipelineScope { Url = $"https://{Handle}/ru/fashion" },
            Limits = new PipelineLimits { MaxPages = 5 },
            Schedules =
            [
                new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6) },
                new ScheduleDeclaration { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = TimeSpan.FromDays(1) },
            ],
        }));

    /// <summary>Writes a run row as the engine leaves it, so the case controls its status, counters and outcome.</summary>
    private static async Task<Guid> RunAsync(SourceHost host, Guid pipeline, string kind, string status, string? reason, int captured,
        int discovered, DateTimeOffset queuedAt, DateTimeOffset? finishedAt, string? outcome = null, string? message = null)
    {
        var id = Guid.CreateVersion7(queuedAt);
        await host.ExecuteAsync("""
            INSERT INTO harvest_runs(id,container_id,pipeline_id,kind,trigger,scheduled_for,status,completion_reason,captured,discovered,
                fetch_outcome,message,queued_at,started_at,finished_at)
            SELECT @id,container_id,id,@kind,'manual',@queued,@status,NULLIF(@reason,''),@captured,@discovered,NULLIF(@outcome,''),NULLIF(@message,''),
                @queued,@queued,CASE WHEN @finished THEN @finishedAt END
            FROM harvest_pipelines WHERE id=@pipeline
            """, ("id", id), ("kind", kind), ("queued", queuedAt), ("status", status), ("reason", reason ?? ""), ("captured", captured),
            ("discovered", discovered), ("outcome", outcome ?? ""), ("message", message ?? ""), ("finished", finishedAt is not null),
            ("finishedAt", finishedAt ?? queuedAt), ("pipeline", pipeline));
        return id;
    }
}
