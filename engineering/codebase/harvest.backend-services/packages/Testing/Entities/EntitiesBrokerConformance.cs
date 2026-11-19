using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Entities;

/// <summary>Specifies the entity reads shared by the in-process module, the HTTP form and the fake: an event searched
/// with its years, its occurrences by year, a year's posts across sources page by page, the calendar with the predicted
/// run, and the change feed's opaque, replaying cursor — each inside one container.</summary>
/// <remarks>Each case makes its own tenant and private container, saves posts through <see cref="Posts"/>, and seeds two
/// entities through <see cref="SeedAsync"/>: Visa Fashion Week (2023 and 2024 held, 2025 scheduled, 2026 predicted;
/// five posts from two sources, one of 2022 about no occurrence) and Samarkand Craft Fair (2025, one post).</remarks>
public abstract class EntitiesBrokerConformance
{
    private const string Visa = "Visa Fashion Week Tashkent";
    private const string Fair = "Samarkand Craft Fair";
    private const string FashionDomain = "style-fashion/womens-fashion";
    private const string CraftDomain = "hobbies-interests/arts-and-crafts";

    /// <summary>Gets the registry that creates each case's tenant, container and sources.</summary>
    protected abstract IRegistrationBroker Registry { get; }

    /// <summary>Gets the post store each case saves its posts through.</summary>
    protected abstract IPostBroker Posts { get; }

    /// <summary>Gets the entity reads under test.</summary>
    protected abstract IEntitiesBroker Entities { get; }

    /// <summary>Gets the implementation's clock.</summary>
    protected abstract TimeProvider Clock { get; }

    /// <summary>Writes an entity into the implementation's store as resolution would, with its creation events.</summary>
    /// <param name="seed">The entity, its occurrences and its bound posts.</param>
    protected abstract Task SeedAsync(EntitySeed seed);

    /// <summary>A search finds the event by name with the years it has something for, the predicted one marked.</summary>
    [Fact]
    public async Task Search_ShouldReturnTheEventWithItsYearsAndCounts()
    {
        var context = await ContextAsync();

        var hits = Value(await Entities.SearchAsync(context.Container, "Visa Fashion Week"));

        var hit = hits.Should().ContainSingle().Which;
        hit.Id.Should().Be(context.Visa);
        hit.ContainerId.Should().Be(context.Container);
        hit.EntityType.Should().Be(EntityTypes.Event);
        hit.Name.Should().Be(Visa);
        hit.PrimaryDomain.Should().Be(FashionDomain);
        hit.Cadence.Should().Be("annual");
        hit.Score.Should().BeInRange(0.35m, 1m);
        hit.Years.Select(year => (year.Year, year.Occurrences, year.Predicted, year.Posts)).Should().Equal(
            (2022, 0L, false, 1L), (2023, 1L, false, 1L), (2024, 1L, false, 2L), (2025, 1L, false, 1L), (2026, 0L, true, 0L));
        hit.Predicted.Should().Be(new DateOnly(2026, 10, 19));
        (hit.Occurrences, hit.Posts, hit.Sources).Should().Be((3L, 5L, 2L));

        Value(await Entities.SearchAsync(context.Container, "visa fashion week", domain: "style-fashion")).Select(found => found.Id)
            .Should().Equal(context.Visa);
        Value(await Entities.SearchAsync(context.Container, "Visa Fashion Week", domain: "hobbies-interests")).Should().BeEmpty();
        Value(await Entities.SearchAsync(context.Container, "Visa Fashion Week", EntityTypes.Listing)).Should().BeEmpty();
        var listed = Value(await Entities.SearchAsync(context.Container, null, domain: "hobbies-interests")).Should().ContainSingle().Which;
        listed.Id.Should().Be(context.Fair);
        listed.Score.Should().BeNull();
        listed.Years.Select(year => year.Year).Should().Equal(2025);
    }

    /// <summary>An entity reads with its occurrences by year: dates, status, place and the predicted run.</summary>
    [Fact]
    public async Task Get_ShouldGroupOccurrencesByYear_AndMarkThePredictedRun()
    {
        var context = await ContextAsync();

        var timeline = Value(await Entities.GetAsync(context.Container, context.Visa));

        timeline.Id.Should().Be(context.Visa);
        timeline.RequestedId.Should().BeNull();
        timeline.Name.Should().Be(Visa);
        timeline.Aliases.Should().Contain("Toshkent moda haftaligi");
        (timeline.Posts, timeline.Sources).Should().Be((5L, 2L));
        timeline.Years.Select(year => (year.Year, year.Posts)).Should().Equal((2022, 1L), (2023, 1L), (2024, 2L), (2025, 1L), (2026, 0L));
        timeline.Years[0].Occurrences.Should().BeEmpty("a post of 2022 names no occurrence");
        var held = timeline.Years[2].Occurrences.Should().ContainSingle().Which;
        held.Id.Should().Be(context.Occurrences[2024]);
        (held.Starts, held.Ends, held.Precision, held.Certainty, held.Status, held.Predicted)
            .Should().Be((new DateOnly(2024, 10, 14), new DateOnly(2024, 10, 19), "day", "stated", "held", false));
        held.Place.Should().Be(new OccurrencePlaceModel { Venue = "Uzexpocentre", City = "Tashkent", Country = "Uzbekistan" });
        var predicted = timeline.Years[4].Occurrences.Should().ContainSingle().Which;
        (predicted.Predicted, predicted.Certainty, predicted.Status, predicted.Starts).Should().Be((true, "estimated", "scheduled", new DateOnly(2026, 10, 19)));

        Error(await Entities.GetAsync(context.Container, Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
        Error(await Entities.GetAsync(await ContainerAsync(), context.Visa)).Type.Should().Be(AppErrorType.NotFound, "another container holds no such entity");
    }

    /// <summary>A year's posts come from every source, newest posted first, page by page; a role keeps its posts only.</summary>
    [Fact]
    public async Task ListPosts_ShouldPageAYearsPostsAcrossSources()
    {
        var context = await ContextAsync();

        var first = Value(await Entities.ListPostsAsync(context.Container, context.Visa, year: 2024, limit: 1));
        var second = Value(await Entities.ListPostsAsync(context.Container, context.Visa, year: 2024, limit: 1, cursor: first.Next));

        first.HasMore.Should().BeTrue();
        second.HasMore.Should().BeFalse();
        var report = first.Data.Should().ContainSingle().Which;
        var announcement = second.Data.Should().ContainSingle().Which;
        (report.PostId, report.Role, report.Year, report.Title, report.PostedAt, report.OccurrenceId)
            .Should().Be((context.ReportB, EntityRoles.Report, 2024, "Visa Fashion Week 2024: the shows", "2024-10-20", context.Occurrences[2024]));
        (announcement.PostId, announcement.Role, announcement.Title).Should().Be((context.AnnounceA, EntityRoles.Announcement, "Visa Fashion Week 2024 opens"));
        new[] { report.Source, announcement.Source }.Should().BeEquivalentTo(context.SourceB.Handle, context.SourceA.Handle);
        new[] { report.SourceId, announcement.SourceId }.Should().BeEquivalentTo([context.SourceB.Id, context.SourceA.Id]);
        report.Link.Should().Be($"https://{context.SourceB.Handle}/shows-2024");
        Value(await Entities.ListPostsAsync(context.Container, context.Visa, year: 2024, cursor: second.Next)).Data.Should().BeEmpty();

        var all = Value(await Entities.ListPostsAsync(context.Container, context.Visa));
        all.Data.Select(post => post.PostId).Should().Equal(context.ReturnA, context.ReportB, context.AnnounceA, context.HistoryB, context.TeaserA);
        all.HasMore.Should().BeFalse();
        var mentions = Value(await Entities.ListPostsAsync(context.Container, context.Visa, role: "Mention")).Data;
        mentions.Select(post => (post.PostId, post.Year, post.PostedAt, post.OccurrenceId)).Should().Equal(
            (context.HistoryB, 2023, "2023-10", context.Occurrences[2023]), (context.TeaserA, 2022, "2022-05-01", (Guid?)null));
        Value(await Entities.ListPostsAsync(context.Container, context.Visa, year: 2022)).Data.Select(post => post.PostId).Should().Equal(context.TeaserA);
        Value(await Entities.ListPostsAsync(context.Container, context.Visa, year: 2030)).Data.Should().BeEmpty();

        var fairCursor = Value(await Entities.ListPostsAsync(context.Container, context.Fair, limit: 1)).Next;
        Error(await Entities.ListPostsAsync(context.Container, context.Visa, cursor: fairCursor)).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.ListPostsAsync(context.Container, context.Visa, year: 1800)).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.ListPostsAsync(context.Container, context.Visa, role: "rumour")).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.ListPostsAsync(context.Container, context.Visa, limit: 0)).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.ListPostsAsync(await ContainerAsync(), context.Visa)).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>The calendar lists a window's occurrences earliest first, the predicted run among them.</summary>
    [Fact]
    public async Task Calendar_ShouldListTheWindow_WithThePredictedRunMarked()
    {
        var context = await ContextAsync();

        var next = Value(await Entities.CalendarAsync(context.Container, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        var year = Value(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));

        var predicted = next.Should().ContainSingle().Which;
        (predicted.EntityId, predicted.Name, predicted.Cadence, predicted.Occurrence.Predicted, predicted.Occurrence.Starts, predicted.Posts)
            .Should().Be((context.Visa, Visa, "annual", true, new DateOnly(2026, 10, 19), 0L));
        year.Select(entry => (entry.EntityId, entry.Occurrence.Starts, entry.Posts))
            .Should().Equal((context.Fair, new DateOnly(2025, 5, 10), 1L), (context.Visa, new DateOnly(2025, 10, 20), 1L));
        Value(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), domain: "style-fashion"))
            .Select(entry => entry.EntityId).Should().Equal(context.Visa);
        Value(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 10, 23), new DateOnly(2025, 11, 30)))
            .Select(entry => entry.Occurrence.Id).Should().Equal([context.Occurrences[2025]], "a window that starts mid-run still holds it");
        Value(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), entityType: EntityTypes.Listing))
            .Should().BeEmpty();

        Error(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1))).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 1, 1), new DateOnly(2026, 2, 1))).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.CalendarAsync(context.Container, new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 1), entityType: "news"))
            .Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>The change feed pages in log order with an opaque cursor; an old cursor replays, the last one stays put.</summary>
    [Fact]
    public async Task PullEvents_ShouldPageAndReplay_WithAnOpaqueCursor()
    {
        var context = await ContextAsync();

        var all = await PollEventsAsync(context.Container, page => page.Data.Count >= 7);

        all.Data.Select(change => (change.Type, change.EntityId)).Should().Equal(
            (EntityEventTypes.EntityCreated, context.Visa), (EntityEventTypes.OccurrenceAdded, context.Visa), (EntityEventTypes.OccurrenceAdded, context.Visa),
            (EntityEventTypes.OccurrenceAdded, context.Visa), (EntityEventTypes.OccurrencePredicted, context.Visa),
            (EntityEventTypes.EntityCreated, context.Fair), (EntityEventTypes.OccurrenceAdded, context.Fair));
        all.HasMore.Should().BeFalse();
        all.Data.Should().OnlyContain(change => change.ContainerId == context.Container);
        all.Data.Select(change => change.EventId).Should().OnlyHaveUniqueItems();
        var created = all.Data[0];
        (created.Name, created.EntityType, created.PrimaryDomain, created.Cadence, created.Status).Should().Be((Visa, EntityTypes.Event, FashionDomain, "annual", "active"));
        all.Data[4].OccurrenceId.Should().Be(context.Occurrences[2026]);
        all.Data[4].Occurrence!.Predicted.Should().BeTrue();
        all.Data[1].Occurrence!.Place!.City.Should().Be("Tashkent");

        var first = Value(await Entities.PullEventsAsync(context.Container, limit: 3));
        var second = Value(await Entities.PullEventsAsync(context.Container, first.Next, 3));
        var third = Value(await Entities.PullEventsAsync(context.Container, second.Next, 3));
        var end = Value(await Entities.PullEventsAsync(context.Container, third.Next, 3));
        var replay = Value(await Entities.PullEventsAsync(context.Container, first.Next, 3));

        (first.HasMore, second.HasMore, third.HasMore, end.HasMore).Should().Be((true, true, false, false));
        first.Data.Concat(second.Data).Concat(third.Data).Select(change => change.EventId).Should().Equal(all.Data.Select(change => change.EventId));
        end.Data.Should().BeEmpty();
        end.Next.Should().Be(third.Next, "nothing new arrived");
        replay.Data.Select(change => change.EventId).Should().Equal(second.Data.Select(change => change.EventId));

        Value(await Entities.PullEventsAsync(context.Container, entityType: EntityTypes.Listing)).Data.Should().BeEmpty();
        Value(await Entities.PullEventsAsync(context.Container, domain: "hobbies-interests")).Data.Select(change => change.EntityId)
            .Should().Equal(context.Fair, context.Fair);
        Error(await Entities.PullEventsAsync(context.Container, "not-a-cursor")).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.PullEventsAsync(await ContainerAsync(), first.Next)).Type.Should().Be(AppErrorType.Validation, "a cursor belongs to its container");
        Error(await Entities.PullEventsAsync(context.Container, limit: 0)).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.PullEventsAsync(context.Container, limit: EntityRouteConstants.MaxEventsLimit + 1)).Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>Reads of an unknown container and malformed searches are refused.</summary>
    [Fact]
    public async Task Reads_ShouldRefuseAnUnknownContainer_AndMalformedFilters()
    {
        var context = await ContextAsync();
        var unknown = Guid.NewGuid();

        Error(await Entities.SearchAsync(unknown, "Visa")).Type.Should().Be(AppErrorType.NotFound);
        Error(await Entities.CalendarAsync(unknown, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2))).Type.Should().Be(AppErrorType.NotFound);
        Error(await Entities.PullEventsAsync(unknown)).Type.Should().Be(AppErrorType.NotFound);
        Error(await Entities.SearchAsync(context.Container, "Visa", limit: 0)).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.SearchAsync(context.Container, "Visa", "news")).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.SearchAsync(context.Container, "Visa", domain: "Style Fashion!")).Type.Should().Be(AppErrorType.Validation);
        Error(await Entities.SearchAsync(context.Container, new string('v', EntityQueryRules.MaxQueryLength + 1))).Type.Should().Be(AppErrorType.Validation);
    }

    private async Task<Context> ContextAsync()
    {
        var unique = Guid.NewGuid().ToString("N")[..12];
        var container = await ContainerAsync();
        var sourceA = await SourceAsync(container, $"a-{unique}.example");
        var sourceB = await SourceAsync(container, $"b-{unique}.example");
        var announceA = await PostAsync(container, sourceA, "opens-2024", "Visa Fashion Week 2024 opens", "2024-10-10");
        var reportB = await PostAsync(container, sourceB, "shows-2024", "Visa Fashion Week 2024: the shows", "2024-10-20");
        var returnA = await PostAsync(container, sourceA, "returns-2025", "Visa Fashion Week returns", "2025-09-01");
        var historyB = await PostAsync(container, sourceB, "history", "How Tashkent's fashion week began", "2023-10");
        var teaserA = await PostAsync(container, sourceA, "teaser", "A fashion week for Tashkent?", "2022-05-01");
        var fairA = await PostAsync(container, sourceA, "fair-2025", "Samarkand craft fair in May", "2025-04-01");
        var now = Clock.GetUtcNow();
        var occurrences = new Dictionary<int, Guid> { [2023] = Guid.NewGuid(), [2024] = Guid.NewGuid(), [2025] = Guid.NewGuid(), [2026] = Guid.NewGuid() };
        var place = new OccurrencePlaceModel { Venue = "Uzexpocentre", City = "Tashkent", Country = "Uzbekistan" };
        var visa = new EntitySeed
        {
            ContainerId = container, Name = Visa, Aliases = ["Toshkent moda haftaligi"], PrimaryDomain = FashionDomain, Cadence = "annual", SeenAtUtc = now,
            Occurrences =
            [
                Occurrence(occurrences[2023], new DateOnly(2023, 10, 16), "held", place),
                Occurrence(occurrences[2024], new DateOnly(2024, 10, 14), "held", place),
                Occurrence(occurrences[2025], new DateOnly(2025, 10, 20), "scheduled", place),
                Occurrence(occurrences[2026], new DateOnly(2026, 10, 19), "scheduled", place) with { Certainty = "estimated", Predicted = true },
            ],
            Relations =
            [
                Relation(announceA, occurrences[2024], EntityRoles.Announcement, now),
                Relation(reportB, occurrences[2024], EntityRoles.Report, now),
                Relation(returnA, occurrences[2025], EntityRoles.Announcement, now),
                Relation(historyB, occurrences[2023], EntityRoles.Mention, now),
                new EntityRelationSeed { PostId = teaserA, Role = EntityRoles.Mention, BoundAtUtc = now },
            ],
        };
        var fairOccurrence = Guid.NewGuid();
        var fair = new EntitySeed
        {
            ContainerId = container, Name = Fair, PrimaryDomain = CraftDomain, SeenAtUtc = now.AddMinutes(-1),
            Occurrences = [Occurrence(fairOccurrence, new DateOnly(2025, 5, 10), "scheduled", null) with { Ends = new DateOnly(2025, 5, 12) }],
            Relations = [Relation(fairA, fairOccurrence, EntityRoles.Announcement, now)],
        };
        await SeedAsync(visa);
        await SeedAsync(fair);
        return new Context(container, sourceA, sourceB, visa.Id, fair.Id, occurrences, announceA, reportB, returnA, historyB, teaserA);
    }

    private async Task<Guid> ContainerAsync()
    {
        var tenant = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "entities-" + Guid.NewGuid().ToString("N"), Name = "Entities conformance" }));
        return Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id })).Id;
    }

    private async Task<SourceModel> SourceAsync(Guid container, string handle) =>
        Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));

    private async Task<Guid> PostAsync(Guid container, SourceModel source, string key, string title, string? postedAt)
    {
        string url = $"https://{source.Handle}/{key}";
        var capture = new Capture
        {
            ExternalKey = key, Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = Clock.GetUtcNow().ToUniversalTime(),
            Body = Encoding.UTF8.GetBytes(title), ContentProjection = title, ContentType = "text/html", HttpStatus = 200,
        };
        var envelope = new PostEnvelope
        {
            Title = title, Text = title, Kind = PostKind.Event, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
            PostedAt = postedAt is null ? null : new PostTimestamp
            {
                Value = postedAt, Precision = postedAt.Length switch { 4 => DatePrecision.Year, 7 => DatePrecision.Month, _ => DatePrecision.Day },
            },
        };
        return Value(await Posts.SaveCaptureAsync(container, source.Id, null, capture, envelope)).Post.Id;
    }

    private async Task<CursorPage<EntityEvent>> PollEventsAsync(Guid container, Func<CursorPage<EntityEvent>, bool> ready)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = Value(await Entities.PullEventsAsync(container));
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(5))
                return page;
            await Task.Delay(50);
        }
    }

    private static OccurrenceModel Occurrence(Guid id, DateOnly starts, string status, OccurrencePlaceModel? place) => new()
    {
        Id = id, Label = starts.Year.ToString(System.Globalization.CultureInfo.InvariantCulture), Starts = starts, Ends = starts.AddDays(5), Precision = "day",
        Certainty = "stated", Status = status, Place = place,
    };

    private static EntityRelationSeed Relation(Guid postId, Guid occurrenceId, string role, DateTimeOffset at) =>
        new() { PostId = postId, OccurrenceId = occurrenceId, Role = role, BoundAtUtc = at };

    private static T Value<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private static AppError Error<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Failure failure ? failure.Error : throw new Xunit.Sdk.XunitException("Expected failure.");

    private sealed record Context(Guid Container, SourceModel SourceA, SourceModel SourceB, Guid Visa, Guid Fair, Dictionary<int, Guid> Occurrences,
        Guid AnnounceA, Guid ReportB, Guid ReturnA, Guid HistoryB, Guid TeaserA);
}
