using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using static Harvest.Tests.Integration.Entities.EntitiesHost;

namespace Harvest.Tests.Integration.Entities;

/// <summary>The entity reads (step D) over PostgreSQL on entities resolution made from scripted extraction rows: an
/// event's years and every source's posts per year, the calendar with its predicted run, the change feed every change
/// appends to and its replaying cursor, and isolation between containers.</summary>
public sealed class EntityReadTests
{
    private const string Tfw = "Tashkent Fashion Week";

    [Fact]
    public async Task Search_ShouldReturnTheYears_AndAYearsPostsShouldSpanTwoSources()
    {
        await using var host = await StartAsync();
        var moda = await host.SourceAsync("moda.uz");
        var (first, created) = await host.EventAsync("tfw-2025", Event(Tfw, "Toshkent moda haftaligi", [Run("2025-10-20", "2025-10-25")]));
        var (second, _) = await host.EventAsync("nedelya", Event(Tfw, "Неделя моды в Ташкенте", [Run("2026-10-19", "2026-10-24")], city: "Ташкент"),
            moda, "moda.uz");
        var (third, _) = await host.EventAsync("tfw-2026", Event(Tfw, "Tashkent Fashion Week 2026", [Run("2026-10-19", "2026-10-24")]));
        var entityId = created.EntityId!.Value;

        var hit = (await host.Reads().SearchAsync(host.ContainerId, "Неделя моды Ташкент", EntityTypes.Event, "style-fashion")).ValueOrThrow()
            .Should().ContainSingle().Which;

        hit.Id.Should().Be(entityId);
        hit.Aliases.Should().Contain("Неделя моды в Ташкенте");
        hit.Years.Select(year => (year.Year, year.Occurrences, year.Predicted, year.Posts))
            .Should().Equal((2025, 1L, false, 1L), (2026, 1L, false, 2L), (2027, 0L, true, 0L));
        (hit.Posts, hit.Sources, hit.Occurrences, hit.Predicted).Should().Be((3L, 2L, 2L, new DateOnly(2027, 10, 19)));

        var year = (await host.Reads().ListPostsAsync(host.ContainerId, entityId, 2026)).ValueOrThrow();
        year.Data.Select(post => post.PostId).Should().BeEquivalentTo([second, third]);
        year.Data.Select(post => post.Source).Should().BeEquivalentTo(["moda.uz", "mstar.uz"], "the year's posts come from both sources");
        year.Data.Should().OnlyContain(post => post.Year == 2026 && post.Role == EntityRoles.Announcement && post.OccurrenceId != null);
        year.Data.Select(post => post.Link).Should().BeEquivalentTo(["https://moda.uz/nedelya", "https://mstar.uz/tfw-2026"]);
        year.Data.Select(post => post.Title).Should().BeEquivalentTo(["nedelya", "tfw-2026"], "titles only, never the text");
        (await host.Reads().ListPostsAsync(host.ContainerId, entityId, 2025)).ValueOrThrow().Data.Select(post => post.PostId).Should().Equal(first);

        var timeline = (await host.Reads().GetAsync(host.ContainerId, entityId)).ValueOrThrow();
        timeline.Years.Select(item => (item.Year, item.Posts, item.Occurrences.Count)).Should().Equal((2025, 1L, 1), (2026, 2L, 1), (2027, 0L, 1));
        timeline.Years[2].Occurrences[0].Should().BeEquivalentTo(new { Predicted = true, Certainty = "estimated", Starts = new DateOnly(2027, 10, 19) });
        timeline.Years[0].Occurrences[0].Place!.City.Should().Be("Tashkent");
    }

    [Fact]
    public async Task Calendar_ShouldIncludeThePredictedRun_EarliestFirst()
    {
        await using var host = await StartAsync();
        var (_, created) = await host.EventAsync("tfw-2025", Event(Tfw, null, [Run("2025-10-20", "2025-10-25")]));
        await host.EventAsync("tfw-2026", Event(Tfw, null, [Run("2026-10-19", "2026-10-24")]));
        await host.EventAsync("fair", Event("Samarkand Craft Fair", null, [Run("2026-05-10", "2026-05-12")], city: "Samarkand"));

        var year = (await host.Reads().CalendarAsync(host.ContainerId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).ValueOrThrow();
        var next = (await host.Reads().CalendarAsync(host.ContainerId, new DateOnly(2027, 6, 1), new DateOnly(2028, 5, 31))).ValueOrThrow();

        year.Select(entry => (entry.Name, entry.Occurrence.Starts, entry.Occurrence.Predicted, entry.Posts))
            .Should().Equal(("Samarkand Craft Fair", new DateOnly(2026, 5, 10), false, 1L), (Tfw, new DateOnly(2026, 10, 19), false, 1L));
        var predicted = next.Should().ContainSingle().Which;
        (predicted.EntityId, predicted.Cadence, predicted.Occurrence.Predicted, predicted.Occurrence.Starts, predicted.Posts)
            .Should().Be((created.EntityId!.Value, "annual", true, new DateOnly(2027, 10, 19), 0L));
    }

    [Fact]
    public async Task Feed_ShouldAppendEveryChangeOfAnEvent_AndPageAndReplayItsCursor()
    {
        await using var host = await StartAsync();
        var (_, created) = await host.EventAsync("tfw-2025", Event(Tfw, null, [Run("2025-10-20", "2025-10-25")]));
        var (second, _) = await host.EventAsync("tfw-2026", Event(Tfw, null, [Run("2026-10-19", "2026-10-24")]));
        await host.EventAsync("tfw-2027", Event(Tfw, null, [Run("2027-10-18", "2027-10-23")]));
        var (postponer, _) = await host.EventAsync("moved", Event(Tfw, null, [Run("2027-11-15", "2027-11-20")], role: "update", changes: ["dates", "status"],
            relationStatus: "postponed", status: "postponed"));
        var entityId = created.EntityId!.Value;

        var all = await PollAsync(host, page => page.Data.Count >= 8);

        all.Data.Take(8).Select(change => change.Type).Should().Equal(
            EntityEventTypes.EntityCreated, EntityEventTypes.OccurrenceAdded,
            EntityEventTypes.OccurrenceAdded, EntityEventTypes.OccurrencePredicted, EntityEventTypes.EntityChanged,
            EntityEventTypes.OccurrenceConfirmed, EntityEventTypes.OccurrencePredicted,
            EntityEventTypes.OccurrenceChanged);
        all.Data.Should().OnlyContain(change => change.EntityId == entityId && change.ContainerId == host.ContainerId);
        all.Data.Skip(8).Select(change => change.Type).Should().BeSubsetOf(
            [EntityEventTypes.OccurrencePredicted, EntityEventTypes.OccurrenceRemoved, EntityEventTypes.EntityChanged], "the postponement moves the prediction");
        all.Data[2].PostId.Should().Be(second);
        all.Data[3].Occurrence!.Starts.Should().Be(new DateOnly(2027, 10, 19));
        all.Data[4].Changes.Should().Equal("cadence");
        all.Data[4].Cadence.Should().Be("annual");
        var confirmed = all.Data[5];
        (confirmed.OccurrenceId, confirmed.Previous!.Predicted, confirmed.Occurrence!.Predicted, confirmed.Occurrence.Starts)
            .Should().Be((all.Data[3].OccurrenceId, true, false, new DateOnly(2027, 10, 18)));
        confirmed.Changes.Should().Equal("dates");
        all.Data[6].Occurrence!.Starts.Should().Be(new DateOnly(2028, 10, 18));
        var postponed = all.Data[7];
        (postponed.PostId, postponed.OccurrenceId, postponed.Occurrence!.Status, postponed.Previous!.Starts, postponed.Occurrence.Starts)
            .Should().Be((postponer, confirmed.OccurrenceId, "postponed", new DateOnly(2027, 10, 18), new DateOnly(2027, 11, 15)));
        postponed.Changes.Should().Equal("dates", "status");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entity_events WHERE payload::text LIKE '%evidence%' OR payload::text LIKE '%Kirish%'"))
            .Should().Be(0, "events carry no post text");

        var pages = new List<CursorPage<EntityEvent>> { (await host.Reads().PullEventsAsync(host.ContainerId, limit: 3)).ValueOrThrow() };
        while (pages[^1].HasMore)
            pages.Add((await host.Reads().PullEventsAsync(host.ContainerId, pages[^1].Next, 3)).ValueOrThrow());
        pages.SelectMany(page => page.Data).Select(change => change.EventId).Should().Equal(all.Data.Select(change => change.EventId));
        var replay = (await host.Reads().PullEventsAsync(host.ContainerId, pages[0].Next, 3)).ValueOrThrow();
        replay.Data.Select(change => change.EventId).Should().Equal(pages[1].Data.Select(change => change.EventId));
        var end = (await host.Reads().PullEventsAsync(host.ContainerId, pages[^1].Next)).ValueOrThrow();
        (end.Data.Count, end.Next, end.HasMore).Should().Be((0, pages[^1].Next, false));
        (await host.Reads().PullEventsAsync(host.ContainerId, "AAAA")).Should().BeOfType<Result<CursorPage<EntityEvent>>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Validation);
    }

    [Fact]
    public async Task Feed_ShouldRecordMergeSplitAndAListingSold_AndADryRunShouldAppendNothing()
    {
        await using var host = await StartAsync(options => options.CandidateFloor = 0.6);
        var (_, tashkent) = await host.EventAsync("tfw", Event(Tfw, null, [Run("2025-10-20", "2025-10-25")]));
        var (_, samarkand) = await host.EventAsync("sfw", Event(Tfw, null, [Run("2026-04-10")], city: "Samarkand"));
        var (_, uz) = await host.EventAsync("ufw", Event("Uzbekistan Fashion Week", "O'zbekiston moda haftaligi", [Run("2026-10-19", "2026-10-24")]));
        Guid one = tashkent.EntityId!.Value, two = samarkand.EntityId!.Value, three = uz.EntityId!.Value;
        long before = (await PollAsync(host, page => page.Data.Count >= 6)).Data.Count;

        (await host.Entities().MergeAsync(three, one)).ValueOrThrow();
        (await host.Entities().SplitAsync(one, two)).ValueOrThrow();
        (await host.Entities().SplitAsync(two, one)).ValueOrThrow().Created.Should().BeFalse();

        var changes = (await PollAsync(host, page => page.Data.Count >= before + 6)).Data.Skip((int)before).ToList();
        changes.Select(change => (change.Type, change.EntityId)).Should().Equal(
            (EntityEventTypes.EntityMerged, three), (EntityEventTypes.OccurrenceAdded, one), (EntityEventTypes.OccurrencePredicted, one),
            (EntityEventTypes.EntityChanged, one), (EntityEventTypes.EntitySplit, one), (EntityEventTypes.EntitySplit, two));
        (changes[0].RelatedEntityId, changes[0].PostId).Should().Be((one, (Guid?)null));
        changes[1].Occurrence!.Starts.Should().Be(new DateOnly(2026, 10, 19), "the merged entity's run moves to the one that stays");
        changes[3].Changes.Should().Equal("cadence");
        (changes[4].RelatedEntityId, changes[5].RelatedEntityId).Should().Be((two, one));

        var olx = await host.SourceAsync("www.olx.uz");
        const string owner = "https://www.olx.uz/list/user/one/";
        var ad = await host.PostAsync("a1", "Ijaraga 2 xonali kvartira 500", olx, "www.olx.uz", owner);
        await host.ExtractAsync(ad, "extract-listing", Listing(500m), "real-estate/apartments");
        var listing = (await host.Entities().ResolveAsync(ad)).ValueOrThrow().Outcomes.Single().EntityId!.Value;
        var sold = await host.PostAsync("a2", "Kvartira sotildi", olx, "www.olx.uz", owner);
        var answer = JsonNode.Parse(Listing(500m, role: "update"))!;
        answer["relation"]!["status"] = "sold-out";
        await host.ExtractAsync(sold, "extract-listing", answer.ToJsonString(), "real-estate/apartments");
        var dry = await host.PostAsync("a3", "Kvartira 600", olx, "www.olx.uz", owner);
        await host.ExtractAsync(dry, "extract-listing", Listing(600m), "real-estate/apartments");
        (await host.Entities().ResolveAsync(dry, dryRun: true)).ValueOrThrow();
        (await host.Entities().ResolveAsync(sold)).ValueOrThrow().Outcomes.Single().Changes.Should().Contain("status");

        var listings = (await PollAsync(host, page => page.Data.Count >= 2, EntityTypes.Listing)).Data;
        listings.Select(change => (change.Type, change.EntityId, change.Status)).Should().Equal(
            (EntityEventTypes.EntityCreated, listing, "active"), (EntityEventTypes.EntityChanged, listing, "sold"));
        listings[1].Changes.Should().Equal("status");
        (await host.Reads().PullEventsAsync(host.ContainerId, domain: "real-estate")).ValueOrThrow().Data.Should().HaveCount(2);
        (await host.Reads().PullEventsAsync(host.ContainerId, domain: "style-fashion/womens-fashion")).ValueOrThrow().Data
            .Should().OnlyContain(change => change.EntityType == EntityTypes.Event);
    }

    [Fact]
    public async Task Reads_ShouldKeepEachContainersEntities_AndFeedToItself()
    {
        await using var host = await StartAsync();
        var (_, pooled) = await host.EventAsync("tfw-pool", Event(Tfw, null, [Run("2026-10-19", "2026-10-24")]));
        var tenant = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "own", Name = "Own" }));
        var own = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id })).Id;
        var ownSource = PostHost.Value(await host.Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = own, Family = SourceFamily.Site, Handle = "own.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).Id;
        var ownPost = await PrivatePostAsync(host, own, ownSource, "tfw-own");
        await host.ExtractAsync(ownPost, "extract-event", Event(Tfw, null, [Run("2026-10-19", "2026-10-24")]), "style-fashion");
        var mine = (await host.Entities().ResolveAsync(ownPost)).ValueOrThrow().Outcomes.Single();
        mine.Outcome.Should().Be("created", "an entity of another container is no candidate");
        Guid pool = host.ContainerId, poolEntity = pooled.EntityId!.Value, ownEntity = mine.EntityId!.Value;

        (await host.Reads().SearchAsync(pool, Tfw)).ValueOrThrow().Select(hit => hit.Id).Should().Equal(poolEntity);
        (await host.Reads().SearchAsync(own, Tfw)).ValueOrThrow().Select(hit => hit.Id).Should().Equal(ownEntity);
        (await host.Reads().GetAsync(pool, ownEntity)).Should().BeOfType<Result<EntityTimelineModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.NotFound);
        (await host.Reads().ListPostsAsync(own, poolEntity)).Should().BeOfType<Result<CursorPage<EntityPostModel>>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.NotFound);
        (await host.Reads().CalendarAsync(own, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31))).ValueOrThrow()
            .Select(entry => entry.EntityId).Should().Equal(ownEntity);
        (await PollAsync(host, page => page.Data.Count >= 2, container: own)).Data.Should().OnlyContain(change => change.EntityId == ownEntity);
        (await PollAsync(host, page => page.Data.Count >= 2)).Data.Should().OnlyContain(change => change.EntityId == poolEntity);
        var ownCursor = (await host.Reads().PullEventsAsync(own)).ValueOrThrow().Next;
        (await host.Reads().PullEventsAsync(pool, ownCursor)).Should().BeOfType<Result<CursorPage<EntityEvent>>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Validation);
        (await host.Reads().SearchAsync(Guid.NewGuid(), Tfw)).Should().BeOfType<Result<IReadOnlyList<EntitySummaryModel>>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.NotFound);
    }

    private static async Task<CursorPage<EntityEvent>> PollAsync(TagsHost host, Func<CursorPage<EntityEvent>, bool> ready, string? entityType = null,
        Guid? container = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = (await host.Reads().PullEventsAsync(container ?? host.ContainerId, limit: 500, entityType: entityType)).ValueOrThrow();
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(10))
                return page;
            await Task.Delay(50);
        }
    }

    private static async Task<Guid> PrivatePostAsync(TagsHost host, Guid container, Guid source, string key)
    {
        string url = $"https://own.example/{key}";
        var capture = new Capture
        {
            ExternalKey = key, Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = host.Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(key),
            ContentProjection = key, HttpStatus = 200, ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Event, Title = key, Text = key, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        return PostHost.Value(await host.Posts.SaveCaptureAsync(container, source, null, capture, envelope)).Post.Id;
    }
}
