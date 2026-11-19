using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.PostExtraction;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using static Harvest.Tests.Integration.Entities.EntitiesHost;

namespace Harvest.Tests.Integration.Entities;

/// <summary>The entity layer over PostgreSQL with scripted extraction rows: one event across sources and years with a
/// cadence and a predicted next run, roles applied with history, listings deduplicated across authors, unsure matches
/// flagged, and the operator's keep-apart, merge and split.</summary>
public sealed class EntityResolutionTests
{
    private const string Tfw = "Tashkent Fashion Week";

    [Fact]
    public async Task TwoYearsFromTwoSources_ShouldMakeOneEvent_WithAnAnnualCadenceAndAPredictedThird()
    {
        await using var host = await StartAsync();
        var other = await host.SourceAsync("moda.uz");

        var (first, created) = await host.EventAsync("tfw-2025", Event(Tfw, "Toshkent moda haftaligi", [Run("2025-10-20", "2025-10-25")]));
        var (second, bound) = await host.EventAsync("nedelya", Event(Tfw, "Неделя моды в Ташкенте", [Run("2026-10-19", "2026-10-24")], city: "Ташкент"),
            other, "moda.uz");
        var (third, again) = await host.EventAsync("tfw-2026", Event(Tfw, "Tashkent Fashion Week 2026", [Run("2026-10-19", "2026-10-24")]));

        created.Outcome.Should().Be("created");
        bound.Outcome.Should().Be("bound");
        bound.EntityId.Should().Be(created.EntityId!.Value);
        bound.Confidence.Should().Be(1m);
        again.OccurrenceId.Should().Be(bound.OccurrenceId!.Value, "the same edition from another post");
        var entityId = created.EntityId.Value;
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entities")).Should().Be(1);
        (await host.ScalarAsync<string>("""
            SELECT string_agg(label || ':' || starts || ':' || COALESCE(ends::text,'') || ':' || certainty || ':' || predicted || ':' || status, ','
                ORDER BY starts) FROM harvest_entity_occurrences
            """)).Should().Be("2025:2025-10-20:2025-10-25:stated:false:scheduled,2026:2026-10-19:2026-10-24:stated:false:scheduled,"
            + "2027:2027-10-19:2027-10-24:estimated:true:scheduled");
        (await host.ScalarAsync<string>("SELECT cadence || ':' || cadence_confidence FROM harvest_entities")).Should().Be("annual:0.652");

        var detail = (await host.Entities().GetAsync(entityId)).ValueOrThrow();
        detail.Entity.Name.Should().Be(Tfw);
        detail.Entity.NameKey.Should().Be("fashion tashkent week");
        detail.Entity.Aliases.Should().Contain(["Toshkent moda haftaligi", "Неделя моды в Ташкенте", "Tashkent Fashion Week 2026"]);
        detail.Entity.PrimaryDomain.Should().Be("style-fashion/womens-fashion");
        detail.Entity.Facts.GetRawText().Should().NotContain("evidence").And.NotContain("Kirish");
        detail.Years.Select(year => year.Year).Should().Equal(2025, 2026, 2027);
        detail.Relations.Select(relation => (relation.PostId, relation.Role, relation.Source)).Should().Equal(
            (first, "announcement", "mstar.uz"), (second, "announcement", "moda.uz"), (third, "announcement", "mstar.uz"));

        var hits = (await host.Entities().SearchAsync(new EntitySearchRequest { Query = "Неделя моды Ташкент", Domain = "style-fashion" }))
            .ValueOrThrow();
        hits.Should().ContainSingle();
        hits[0].Years.Should().Equal(2025, 2026);
        hits[0].Predicted.Should().Be(new DateOnly(2027, 10, 19));
        hits[0].Posts.Should().Be(3);
        (await host.Entities().SearchAsync(new EntitySearchRequest { Query = "Tashkent Fashion Week", Domain = "real-estate" }))
            .ValueOrThrow().Should().BeEmpty();

        var (_, confirmed) = await host.EventAsync("tfw-2027", Event(Tfw, null, [Run("2027-10-18", "2027-10-23")]));

        confirmed.Outcome.Should().Be("bound");
        (await host.ScalarAsync<string>("""
            SELECT string_agg(starts || ':' || predicted, ',' ORDER BY starts) FROM harvest_entity_occurrences WHERE starts>='2027-01-01'
            """)).Should().Be("2027-10-18:false,2028-10-18:true", "the real run replaces the prediction, and the next one is predicted");
        (await host.ScalarAsync<string>("""
            SELECT h->>'field' FROM harvest_entity_occurrences o, jsonb_array_elements(o.history) h WHERE o.starts='2027-10-18'
            """)).Should().Be("predicted");

        var report = (await host.Entities().ReportAsync()).ValueOrThrow();
        report.Types.Should().Equal(new Dictionary<string, long> { ["event"] = 1 });
        report.Cadences.Should().Equal(new Dictionary<string, long> { ["annual"] = 1 });
        report.Occurrences.Should().Equal(new Dictionary<string, long> { ["predicted"] = 1, ["scheduled"] = 3 });
        report.Roles.Should().Equal(new Dictionary<string, long> { ["announcement"] = 4 });
    }

    [Fact]
    public async Task Updates_ShouldMoveStatusAndDatesWithHistory_AndNeverLetAnEstimateOverrideAStatedDate()
    {
        await using var host = await StartAsync();
        var (_, created) = await host.EventAsync("tfw", Event(Tfw, null, [Run("2026-10-19", "2026-10-24")]));
        var occurrenceId = created.OccurrenceId!.Value;

        var (_, estimate) = await host.EventAsync("rumour", Event(Tfw, null, [Run("2026-11-02", certainty: "estimated")], role: "update",
            changes: ["dates"]));
        estimate.Changes.Should().Equal("dates");
        estimate.OccurrenceId.Should().Be(occurrenceId);
        (await OccurrenceAsync(host, occurrenceId)).Should().Be("2026-10-19:2026-10-24:stated:scheduled:0", "an estimate never moves a stated date");

        var (postponer, postponed) = await host.EventAsync("moved", Event(Tfw, null, [Run("2026-11-16", "2026-11-20")], role: "update",
            changes: ["dates", "status"], relationStatus: "postponed", status: "postponed"));
        postponed.OccurrenceId.Should().Be(occurrenceId);
        (await OccurrenceAsync(host, occurrenceId)).Should().Be("2026-11-16:2026-11-20:stated:postponed:2");
        (await host.ScalarAsync<string>("""
            SELECT string_agg(h->>'field' || ':' || COALESCE(h->'from'->>'starts',h->>'from') || '>' || COALESCE(h->'to'->>'starts',h->>'to'), '|')
            FROM harvest_entity_occurrences o, jsonb_array_elements(o.history) h WHERE o.id=@id
            """, ("id", occurrenceId))).Should().Be("dates:2026-10-19>2026-11-16|status:scheduled>postponed");
        (await host.ScalarAsync<long>("""
            SELECT count(*) FROM harvest_entity_occurrences o, jsonb_array_elements(o.history) h WHERE o.id=@id AND h->>'postId'=@post
            """, ("id", occurrenceId), ("post", postponer.ToString("D")))).Should().Be(2);

        var (canceller, cancelled) = await host.EventAsync("off", Event(Tfw, null, role: "update", changes: ["status"], relationStatus: "cancelled",
            status: "cancelled", editionLabel: "2026"));
        cancelled.OccurrenceId.Should().Be(occurrenceId, "the edition label names the year");
        (await OccurrenceAsync(host, occurrenceId)).Should().Be("2026-11-16:2026-11-20:stated:cancelled:3");
        (await host.ScalarAsync<string>("SELECT role || ':' || array_to_string(changes, ',') FROM harvest_entity_relations WHERE post_id=@post",
            ("post", canceller))).Should().Be("update:status");
    }

    [Fact]
    public async Task Report_ShouldMarkTheEditionHeld_AndAMentionShouldOnlyAttach()
    {
        await using var host = await StartAsync();
        var (_, created) = await host.EventAsync("tfw", Event(Tfw, null, [Run("2025-10-20", "2025-10-25")]));
        string before = await SnapshotAsync(host);

        var (mentioner, mention) = await host.EventAsync("mention", Event(Tfw, null, role: "mention"));

        mention.Outcome.Should().Be("bound");
        mention.Role.Should().Be("mention");
        mention.OccurrenceId.Should().BeNull();
        (await SnapshotAsync(host)).Should().Be(before, "a mention attaches without changing the entity");
        (await host.ScalarAsync<string>("SELECT role || ':' || (occurrence_id IS NULL) FROM harvest_entity_relations WHERE post_id=@post",
            ("post", mentioner))).Should().Be("mention:true");

        var (_, report) = await host.EventAsync("review", Event(Tfw, null, [Run("2025-10-20", "2025-10-25")], role: "report", status: "held"));

        report.OccurrenceId.Should().Be(created.OccurrenceId!.Value);
        (await OccurrenceAsync(host, created.OccurrenceId.Value)).Should().Be("2025-10-20:2025-10-25:stated:held:1");
    }

    [Fact]
    public async Task Listings_ShouldTakeTheSameAuthorsRepostAsAnUpdate_AndAnotherAuthorsAdAsASecondOffer()
    {
        await using var host = await StartAsync();
        var olx = await host.SourceAsync("www.olx.uz");
        const string ownerOne = "https://www.olx.uz/list/user/one/", ownerTwo = "https://www.olx.uz/list/user/two/";

        var first = await host.PostAsync("a1", "Ijaraga 2 xonali kvartira 500", olx, "www.olx.uz", ownerOne);
        await host.ExtractAsync(first, "extract-listing", Listing(500m), "real-estate/apartments");
        var created = (await host.Entities().ResolveAsync(first)).ValueOrThrow().Outcomes.Single();

        var repost = await host.PostAsync("a2", "Ijaraga 2 xonali kvartira 450, shoshilinch", olx, "www.olx.uz", ownerOne);
        await host.ExtractAsync(repost, "extract-listing", Listing(450m), "real-estate/apartments");
        var update = (await host.Entities().ResolveAsync(repost)).ValueOrThrow().Outcomes.Single();

        var rival = await host.PostAsync("b1", "Сдается 2-комнатная квартира 520", olx, "www.olx.uz", ownerTwo);
        await host.ExtractAsync(rival, "extract-listing", Listing(520m, district: "Юнусабадский район", city: "Ташкент", area: 63m),
            "real-estate/apartments");
        var offer = (await host.Entities().ResolveAsync(rival)).ValueOrThrow().Outcomes.Single();

        created.Outcome.Should().Be("created");
        created.Role.Should().Be("announcement");
        update.Outcome.Should().Be("bound");
        update.EntityId.Should().Be(created.EntityId!.Value);
        update.Role.Should().Be("update", "the same author's later ad");
        update.Changes.Should().Equal("price");
        offer.EntityId.Should().Be(created.EntityId.Value);
        offer.Role.Should().Be("announcement", "another author's ad for the same flat");
        offer.Changes.Should().BeEmpty();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entities")).Should().Be(1);
        (await SpreadAsync(host)).Should().Be("USD/month:450-520:2", "each author's latest offer");

        var relisted = await host.ReviseAsync(first, "a1", "extract-listing", Listing(480m), olx, "www.olx.uz", ownerOne, "real-estate/apartments");
        relisted.Role.Should().Be("update", "the same source and external key");
        relisted.Changes.Should().Equal("price");
        (await SpreadAsync(host)).Should().Be("USD/month:480-520:2");

        var detail = (await host.Entities().GetAsync(created.EntityId.Value)).ValueOrThrow();
        detail.Relations.Should().HaveCount(3);
        detail.Relations.Select(relation => relation.Offer!.Value.GetProperty("price").GetProperty("amount").GetDecimal())
            .Should().BeEquivalentTo([480m, 450m, 520m]);
        var offerJson = detail.Relations.Single(relation => relation.PostId == rival).Offer!.Value;
        offerJson.GetProperty("authorKind").GetString().Should().Be("owner");
        offerJson.GetProperty("link").GetString().Should().Be("https://www.olx.uz/b1");
        offerJson.GetProperty("externalKey").GetString().Should().Be("b1");
        offerJson.GetRawText().Should().NotContain("Seller").And.NotContain("user/two");
    }

    [Fact]
    public async Task WeakMatch_ShouldBindNothing_AndRecordAFlagForReview()
    {
        await using var host = await StartAsync(options => options.MinMatchConfidence = 0.95);
        var (_, created) = await host.EventAsync("tfw", Event(Tfw, null, [Run("2026-10-19", "2026-10-24")]));

        var (visa, weak) = await host.EventAsync("visa", Event("Visa Fashion Week Tashkent", null, [Run("2027-04-10")]));

        weak.Outcome.Should().Be("unsure");
        weak.Reason.Should().Be("weak-match");
        weak.EntityId.Should().Be(created.EntityId!.Value);
        weak.Confidence.Should().BeInRange(0.5m, 0.95m);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entity_relations WHERE post_id=@post", ("post", visa))).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entities")).Should().Be(1, "a weak match never creates a duplicate either");
        (await host.Entities().ResolveAsync(visa)).ValueOrThrow();
        var report = (await host.Entities().ReportAsync()).ValueOrThrow();
        report.Flags.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Field = "match", Reason = "weak-match", Count = 1L });
    }

    [Fact]
    public async Task KeepApart_ShouldHoldABindingAcrossResolution_AndMergeAndSplitShouldWork()
    {
        await using var host = await StartAsync(options => options.CandidateFloor = 0.6);
        var (_, tashkent) = await host.EventAsync("tfw", Event(Tfw, null, [Run("2025-10-20", "2025-10-25")]));
        var (post, samarkand) = await host.EventAsync("sfw", Event(Tfw, null, [Run("2026-04-10")], city: "Samarkand"));
        Guid one = tashkent.EntityId!.Value, two = samarkand.EntityId!.Value;
        samarkand.Outcome.Should().Be("created", "another city is another event");

        var drift = await host.ReviseAsync(post, "sfw", "extract-event", Event(Tfw, null, [Run("2025-10-21")], city: null));
        drift.EntityId.Should().Be(one, "with no split, the post's new content matches the Tashkent edition best");
        var back = await host.ReviseAsync(post, "sfw", "extract-event", Event(Tfw, null, [Run("2026-04-10")], city: "Samarkand"));
        back.EntityId.Should().Be(two);

        var split = (await host.Entities().SplitAsync(one, two)).ValueOrThrow();
        split.Created.Should().BeTrue();
        new[] { split.LeftId, split.RightId }.Should().BeEquivalentTo([one, two]);
        (await host.Entities().SplitAsync(two, one)).ValueOrThrow().Created.Should().BeFalse();

        var held = await host.ReviseAsync(post, "sfw", "extract-event", Event(Tfw, null, [Run("2025-10-21")], city: null));
        held.EntityId.Should().Be(two, "an entity kept apart from the post's own is no candidate");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entity_relations WHERE post_id=@post", ("post", post))).Should().Be(1);
        (await host.Entities().MergeAsync(two, one)).Should().BeOfType<Result<EntityMergeModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);

        var (uzPost, uz) = await host.EventAsync("ufw", Event("Uzbekistan Fashion Week", "O'zbekiston moda haftaligi", [Run("2026-10-19", "2026-10-24")]));
        uz.Outcome.Should().Be("created");
        var merge = (await host.Entities().MergeAsync(uz.EntityId!.Value, one)).ValueOrThrow();

        merge.Relations.Should().Be(1);
        merge.Occurrences.Should().Be(1);
        var merged = (await host.Entities().GetAsync(uz.EntityId.Value)).ValueOrThrow();
        merged.RequestedId.Should().Be(uz.EntityId.Value);
        merged.Entity.Id.Should().Be(one);
        merged.Entity.Aliases.Should().Contain(["Uzbekistan Fashion Week", "O'zbekiston moda haftaligi"]);
        merged.Entity.Cadence.Should().Be("annual");
        merged.Years.Select(year => year.Year).Should().Equal(2025, 2026, 2027);
        merged.Relations.Select(relation => relation.PostId).Should().Contain(uzPost);
        (await host.Entities().SearchAsync(new EntitySearchRequest { Query = "Uzbekistan Fashion Week" })).ValueOrThrow()
            .Select(hit => hit.Id).Should().Contain(one).And.NotContain(uz.EntityId.Value);
        (await host.Entities().MergeAsync(uz.EntityId.Value, one)).Should().BeOfType<Result<EntityMergeModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);
        (await host.Entities().SplitAsync(uz.EntityId.Value, two)).Should().BeOfType<Result<EntityKeepApartModel>.Failure>();
        (await host.Entities().ReportAsync()).ValueOrThrow().Should().BeEquivalentTo(new { Merged = 1L, KeptApart = 1L });
    }

    [Fact]
    public async Task Resolve_ShouldRefuseAPostWithoutAnswers_AndWriteNothingOnADryRun_AndBackfillOnlyWhenEnabled()
    {
        await using var host = await StartAsync();
        var bare = await host.PostAsync("bare", "No answer yet.");
        (await host.Entities().ResolveAsync(bare)).Should().BeOfType<Result<EntityResolveModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);
        (await host.Entities().ResolveAsync(Guid.NewGuid())).Should().BeOfType<Result<EntityResolveModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.NotFound);

        await host.ExtractAsync(bare, "extract-event", Event(Tfw, null, [Run("2026-10-19")]), "style-fashion");
        var dry = (await host.Entities().ResolveAsync(bare, dryRun: true)).ValueOrThrow();
        dry.DryRun.Should().BeTrue();
        dry.Outcomes.Single().Outcome.Should().Be("created");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entities")).Should().Be(0);
        (await host.Entities().BackfillAsync(new EntityBackfillRequest())).Should().BeOfType<Result<int>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);

        await using var enabled = await StartAsync(options => options.Enabled = true);
        var waiting = await enabled.PostAsync("waiting", "Answered before entities were enabled.");
        await enabled.ExtractAsync(waiting, "extract-event", Event(Tfw, null, [Run("2026-10-19")]), "style-fashion");

        (await enabled.Entities().BackfillAsync(new EntityBackfillRequest())).ValueOrThrow().Should().Be(1);
        (await enabled.Entities().ProcessDueAsync()).Should().Be(1);
        (await enabled.ScalarAsync<long>("SELECT count(*) FROM harvest_entity_relations WHERE post_id=@post", ("post", waiting))).Should().Be(1);
        (await enabled.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE kind='entity-resolve'")).Should().Be(0);
        (await enabled.Entities().BackfillAsync(new EntityBackfillRequest())).ValueOrThrow().Should().Be(0, "the answer has its relation");
    }

    [Fact]
    public async Task DoneExtraction_ShouldQueueResolution_AndTheWorkerShouldBindThePost()
    {
        await using var host = await TagsHost.StartAsync(configure: services =>
        {
            services.AddHarvestPostExtraction().Configure(options => options.Wanted = [new PostExtractionWant { EntityType = "event", Domain = "style-fashion" }]);
            services.AddHarvestEntities().Configure(options => options.Enabled = true);
        });
        var prompts = host.Scoped.GetRequiredService<IPromptsBroker>();
        (await prompts.PutUseCaseAsync(new UseCaseModel
        {
            Key = PostExtractionConstants.EventUseCase, PromptMode = PromptMode.Versioned, KeepOutputs = true,
            OutputSchema = PostExtractionDefinitions.OutputSchema(PostExtractionConstants.EventUseCase),
        })).ValueOrThrow();
        (await prompts.SavePromptAsync(new PromptDraft
        {
            UseCase = PostExtractionConstants.EventUseCase, SystemTemplate = "Extract.", UserTemplate = "{{title}}\n{{text}}", By = "test",
        })).ValueOrThrow();
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week",
            "Moda haftaligi 12-14 dekabr kunlari Uzexpocentre'da bo'ladi. Kirish 50 000 so'm. Dizaynerlar uchun ro'yxatdan o'tish 1 dekabrgacha davom etadi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer()));
        await host.Tags.ProcessDueAsync();

        (await host.Scoped.GetRequiredService<IPostExtractionBroker>().ProcessDueAsync()).Should().Be(1);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE kind='entity-resolve'")).Should().Be(1);
        (await host.Entities().ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("""
            SELECT e.name || ':' || r.role || ':' || o.starts FROM harvest_entity_relations r JOIN harvest_entities e ON e.id=r.entity_id
            JOIN harvest_entity_occurrences o ON o.id=r.occurrence_id WHERE r.post_id=@post
            """, ("post", saved.Post.Id))).Should().Be("Tashkent Fashion Week:announcement:2026-12-12");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE kind='entity-resolve'")).Should().Be(0);
    }

    private static Task<string> OccurrenceAsync(TagsHost host, Guid id) => host.ScalarAsync<string>("""
        SELECT starts || ':' || COALESCE(ends::text,'') || ':' || certainty || ':' || status || ':' || jsonb_array_length(history)
        FROM harvest_entity_occurrences WHERE id=@id
        """, ("id", id));

    private static Task<string> SnapshotAsync(TagsHost host) => host.ScalarAsync<string>("""
        SELECT (SELECT string_agg(name || facts::text || cadence || status || array_to_string(aliases, '|'), ',') FROM harvest_entities)
            || (SELECT COALESCE(string_agg(o.id || o.starts::text || o.status || o.history::text || o.predicted, ',' ORDER BY o.starts), '')
                FROM harvest_entity_occurrences o)
        """);

    private static Task<string> SpreadAsync(TagsHost host) => host.ScalarAsync<string>("""
        SELECT string_agg((s->>'currency') || '/' || (s->>'period') || ':' || (s->>'min') || '-' || (s->>'max') || ':' || (s->>'offers'), ',')
        FROM harvest_entities e, jsonb_array_elements(e.facts->'priceSpread') s
        """);
}
