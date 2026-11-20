using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.PostExtraction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace Harvest.Tests.Integration.Tagging;

/// <summary>A post whose every source section implies its tags gets them in code with the post write: tier <c>Rules</c> tags, no
/// <c>post-tags</c> call, one revision and one event; extraction queues directly when the source mode allows it, and the
/// answer reaches the post's subscriptions as <c>post.extracted</c> with <c>extras.listingAi</c>. Posts no section implies
/// tags for keep the model path. The ads are invented.</summary>
[Collection("Post delivery transactions")]
public sealed class SectionImpliedTagsTests
{
    private const string Title = "2 xonali kvartira";
    private const string Text = "Ijaraga 2 xonali kvartira. Narxi 500 USD oyiga. Depozit: 500 USD.";
    private const string ApartmentsUrl = "https://www.olx.uz/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/?search%5Border%5D=created_at%3Adesc";
    private const string HousesUrl = "https://www.olx.uz/nedvizhimost/doma/arenda-dolgosrochnaya/tashkent/?search%5Border%5D=created_at%3Adesc";
    private const string OtherUrl = "https://www.olx.uz/nedvizhimost/commercial/?section=other";
    private const string GhostUrl = "https://www.olx.uz/nedvizhimost/ghost/?section=ghost";
    private const string Rule = "rule:section-implied@1";

    [Fact]
    public async Task Write_ShouldTagAnApartmentsAdInCode_AndQueueExtractionDirectly_UnderAuto()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var subscription = await SubscribeAsync(host, pipeline);

        var saved = await olx.SaveAsync(pipeline, "IDabcd", Title, Text);

        var post = saved.Post;
        post.Revision.Should().Be(1, "the rule tags are part of the write that creates the post");
        post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:real-estate/apartments", "domain:real-estate");
        post.Base.Tags.Should().OnlyContain(tag => tag.Tier == ExtractionTier.Rules && tag.Version == "section-implied@1" && tag.Score == null);
        post.Base.Extras.Keys.Should().NotContain("postTags", "a rule writes no English title");
        post.Base.Classification.Should().NotBeNull();
        post.Base.Classification!.Verdict.Should().Be(ClassificationVerdict.Relevant);
        post.Base.Classification.Classifier.Should().Be("rule:section-implied@1");
        post.Base.Classification.Reason.Should().Be("listing under real-estate");
        post.Base.Classification.Kind.Should().Be("listing");

        (await WorkAsync(host, post.Id, "post-tags")).Should().Be(0, "no tagging call is queued");
        (await WorkAsync(host, post.Id, "extract-listing")).Should().Be(1, "extraction is queued on the write");
        (await host.ScalarAsync<string>("""
            SELECT status || '|' || entity_type || '|' || (domains->0->>'node') || '|' || (domains->0->>'primary') || '|' || timeframe || '|'
                || (model IS NULL) || '|' || (prompt_version IS NULL) || '|' || (inference_id IS NULL) || '|' || (title_en IS NULL)
                || '|' || (topics = '{}') || '|' || prompt_hash || '|' || revision
            FROM harvest_post_tags WHERE post_id=@post
            """, ("post", post.Id))).Should().Be($"done|listing|real-estate/apartments|true|evergreen|true|true|true|true|true|{Rule}|1");
        (await host.Tags.ProcessDueAsync()).Should().Be(0);
        host.Caller.Requests.Should().BeEmpty("no model is called for the tags");
        var events = Events(await PullAsync(host, subscription, 1));
        events.Select(item => item.Type).Should().Equal("post.new");
        events[0].Revision.Should().Be(1);
        events[0].Data!.Value.GetProperty("base").GetProperty("tags").GetArrayLength().Should().Be(3, "post.new already carries the rule tags");
    }

    [Fact]
    public async Task Write_ShouldTagAHousesAdUnderItsOwnNode()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);

        var saved = await olx.SaveAsync(await olx.PipelineAsync(HousesUrl), "IDhous", Title, Text);

        saved.Post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:real-estate/houses", "domain:real-estate");
    }

    [Fact]
    public async Task Write_ShouldTagInCodeButQueueNothing_UnderManual_AndLetANamedRunExtract()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host, ModelProcessingMode.Manual);

        var saved = await olx.SaveAsync(await olx.PipelineAsync(ApartmentsUrl), "IDmanu", Title, Text);

        saved.Post.Base.Tags.Should().HaveCount(3);
        (await WorkAsync(host, saved.Post.Id, "post-tags")).Should().Be(0);
        (await WorkAsync(host, saved.Post.Id, "extract-listing")).Should().Be(0, "manual queues only by a named run");
        (await PostExtractionTests.Extraction(host).ProcessDueAsync()).Should().Be(0);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.ListingAnswer()));

        var run = PostHost.Value(await PostExtractionTests.Extraction(host).RunAsync(saved.Post.Id));

        run.Status.Should().Be("done");
        run.UseCase.Should().Be("extract-listing");
        host.Caller.Requests.Should().HaveCount(1, "only the named run reached a model");
    }

    [Fact]
    public async Task Write_ShouldTagInCodeButQueueNothing_UnderOff_AndRefuseANamedRun()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host, ModelProcessingMode.Off);

        var saved = await olx.SaveAsync(await olx.PipelineAsync(ApartmentsUrl), "IDoff0", Title, Text);

        saved.Post.Base.Tags.Should().HaveCount(3, "a rule sends nothing to a model, so the source mode does not keep it away");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE post_id=@post", ("post", saved.Post.Id))).Should().Be(0);
        (await PostExtractionTests.Extraction(host).RunAsync(saved.Post.Id)).Should().BeOfType<Result<PostExtractionRunModel>.Failure>();
        host.Caller.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Write_ShouldKeepTheModelPath_ForPostsNoSectionImpliesTagsFor()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);

        var section = await olx.SaveAsync(await olx.PipelineAsync(OtherUrl), "IDothr", Title, Text);
        var plain = await host.SaveAsync("flat", Title, Text);

        foreach (var post in new[] { section.Post, plain.Post })
        {
            post.Base.Tags.Should().BeEmpty("the model has not tagged the post yet");
            (await WorkAsync(host, post.Id, "post-tags")).Should().Be(1, "tagging is queued as it was");
            (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_tags WHERE post_id=@post", ("post", post.Id))).Should().Be(0);
        }
    }

    [Fact]
    public async Task Write_ShouldKeepTheModelPath_WhenTheDeclaredNodeIsNotInTheTaxonomy()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);

        var saved = await olx.SaveAsync(await olx.PipelineAsync(GhostUrl), "IDghst", Title, Text);

        saved.Post.Base.Tags.Should().BeEmpty();
        (await WorkAsync(host, saved.Post.Id, "post-tags")).Should().Be(1);
    }

    [Fact]
    public async Task Write_ShouldTagNothing_WhenTaggingIsOff()
    {
        await using var host = await StartAsync(enabled: false);
        var olx = await OlxAsync(host);

        var saved = await olx.SaveAsync(await olx.PipelineAsync(ApartmentsUrl), "IDnotg", Title, Text);

        saved.Post.Base.Tags.Should().BeEmpty("with Layer 1 off no tag is written, a rule's included");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE post_id=@post", ("post", saved.Post.Id))).Should().Be(0);
    }

    [Fact]
    public async Task ContentChange_ShouldTagTheNewTextInCode_AndQueueExtractionAgain()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var saved = await olx.SaveAsync(pipeline, "IDedit", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.ListingAnswer()));
        (await PostExtractionTests.Extraction(host).ProcessDueAsync()).Should().Be(1);

        host.Clock.Advance(TimeSpan.FromHours(1));
        var edited = await olx.SaveAsync(pipeline, "IDedit", Title, "Ijaraga 2 xonali kvartira. Narxi 450 USD oyiga. Depozit: 450 USD.");

        edited.Post.Id.Should().Be(saved.Post.Id);
        edited.Post.Revision.Should().Be(2);
        edited.Post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:real-estate/apartments", "domain:real-estate");
        (await WorkAsync(host, saved.Post.Id, "post-tags")).Should().Be(0);
        (await WorkAsync(host, saved.Post.Id, "extract-listing")).Should().Be(1, "the new text has no answer yet");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_tags WHERE post_id=@post AND prompt_hash=@rule AND status='done'",
            ("post", saved.Post.Id), ("rule", Rule))).Should().Be(2, "one rule row per content");
    }

    [Fact]
    public async Task Worker_ShouldGiveAQueuedPostItsTagsInCode_NotAModelCall()
    {
        await using var host = await StartAsync(enabled: false);
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var subscription = await SubscribeAsync(host, pipeline);
        var quiet = await SubscribeAsync(host, pipeline, filterKey: QuietListingFilter.FilterKey);
        var saved = await olx.SaveAsync(pipeline, "IDlate", Title, Text);
        saved.Post.Base.Tags.Should().BeEmpty();
        host.Scoped.GetRequiredService<IOptions<PostTagsOptions>>().Value.Enabled = true;
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest())).ValueOrThrow().Should().Be(1);

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        host.Caller.Requests.Should().BeEmpty("the section already says what the post is");
        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:real-estate/apartments", "domain:real-estate");
        post.Revision.Should().Be(2, "tags an existing post gains are a revision, as any tag write is");
        (await WorkAsync(host, post.Id, "post-tags")).Should().Be(0);
        (await WorkAsync(host, post.Id, "extract-listing")).Should().Be(1);
        Events(await PullAsync(host, subscription, 2)).Select(item => item.Type).Should().Equal("post.new", "post.changed");
        Events(await PullAsync(host, quiet, 1)).Select(item => item.Type).Should().Equal(["post.new"],
            "a filter that ignores tag-only changes, as the marketplace one does, gets no post.changed for tags a backfill wrote");
    }

    [Fact]
    public async Task Retag_ShouldRaiseNoPostChangedForAFilterThatIgnoresTagOnlyChange_ButAnEditStillDoes()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(OtherUrl);
        var plain = await SubscribeAsync(host, pipeline);
        var quiet = await SubscribeAsync(host, pipeline, filterKey: QuietListingFilter.FilterKey);
        var saved = await olx.SaveAsync(pipeline, "IDretg", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer("listing", [new { node = "real-estate/apartments", primary = true }], ["rent"], timeframe: "evergreen")));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id)).Revision.Should().Be(2, "a model's tags are a revision");
        Events(await PullAsync(host, plain, 2)).Select(item => item.Type).Should().Equal("post.new", "post.changed");
        Events(await PullAsync(host, quiet, 1)).Select(item => item.Type).Should().Equal("post.new");

        host.Clock.Advance(TimeSpan.FromHours(1));
        await olx.SaveAsync(pipeline, "IDretg", Title, "Ijaraga 2 xonali kvartira. Narxi 450 USD oyiga. Depozit: 450 USD.");

        Events(await PullAsync(host, quiet, 2)).Select(item => item.Type).Should().Equal("post.new", "post.changed");
        Events(await PullAsync(host, quiet, 2))[1].Revision.Should().Be(3, "a new text is a content change and goes out");
    }

    [Fact]
    public async Task Selector_ShouldJudgeARuleTaggedAd_AndMatchItByEntityType()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var listings = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "haven-listings",
            Selector = new SubscriptionSelector { EntityTypes = ["listing"] },
        }));
        var events = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "events-only",
            Selector = new SubscriptionSelector { EntityTypes = ["event"] },
        }));

        var saved = await olx.SaveAsync(pipeline, "IDslct", Title, Text);

        var delivered = Events(await PullAsync(host, listings.Id, 1));
        delivered.Select(item => item.Type).Should().Equal("post.new");
        delivered[0].PostId.Should().Be(saved.Post.Id);
        delivered[0].Match.Should().NotBeNull();
        delivered[0].Match!.Verdict.Should().Be(SelectorVerdict.Match, "the rule's tags are an answer the selector reads");
        delivered[0].Match!.Clauses.Should().Equal("type:listing");
        Events(await PullAsync(host, events.Id)).Should().BeEmpty("the ad is judged a listing, not an event");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@subscription",
            ("subscription", events.Id))).Should().Be(0);
    }

    [Fact]
    public async Task Write_ShouldTagInCodeButQueueNoExtraction_WhenNoDemandWantsThePair()
    {
        await using var host = await StartAsync(wanted: [PostExtractionTests.Want("event", "style-fashion")]);
        var olx = await OlxAsync(host);

        var saved = await olx.SaveAsync(await olx.PipelineAsync(ApartmentsUrl), "IDnowt", Title, Text);

        saved.Post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:real-estate/apartments", "domain:real-estate");
        saved.Post.Base.Tags.Should().OnlyContain(tag => tag.Tier == ExtractionTier.Rules);
        (await WorkAsync(host, saved.Post.Id, "post-tags")).Should().Be(0);
        (await WorkAsync(host, saved.Post.Id, "extract-listing")).Should().Be(0, "no demand wants listing + real-estate");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE post_id=@post", ("post", saved.Post.Id))).Should().Be(0);
        host.Caller.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Report_ShouldCountModelAnswersOnly()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        await olx.SaveAsync(await olx.PipelineAsync(ApartmentsUrl), "IDrprt", Title, Text);

        var report = PostHost.Value(await host.Tags.ReportAsync(host.Clock.GetUtcNow().AddDays(-1)));

        report.Statuses.Should().BeEmpty("a rule's tags are no model answer");
        report.EntityTypes.Should().BeEmpty();
        report.Domains.Should().BeEmpty();
    }

    [Fact]
    public async Task Answer_ShouldReachTheSubscriptionsThatHoldThePost_AsPostExtractedWithTheListingAi()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var holder = await SubscribeAsync(host, pipeline);
        var saved = await olx.SaveAsync(pipeline, "IDdone", Title, Text);
        var late = await SubscribeAsync(host, pipeline, BackfillMode.None);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.ListingAnswer()));

        (await PostExtractionTests.Extraction(host).ProcessDueAsync()).Should().Be(1);

        var events = Events(await PullAsync(host, holder, 2));
        events.Select(item => item.Type).Should().Equal("post.new", "post.extracted");
        var extracted = events[1];
        extracted.PostId.Should().Be(saved.Post.Id);
        extracted.Revision.Should().Be(1, "the answer adds no revision");
        var ai = extracted.Data!.Value.GetProperty("base").GetProperty("extras").GetProperty("listingAi");
        ai.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["schema", "promptVersion", "model", "costMicroUsd", "revision", "answer", "flags"]);
        ai.GetProperty("schema").GetString().Should().Be("extract-listing@2");
        ai.GetProperty("promptVersion").GetInt32().Should().Be(1);
        ai.GetProperty("model").GetString().Should().Be("anthropic/claude-haiku-5-5");
        ai.GetProperty("costMicroUsd").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        ai.GetProperty("revision").GetInt32().Should().Be(1, "the revision the answer read");
        ai.GetProperty("answer").GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(500m);
        ai.GetProperty("answer").GetProperty("propertyKind").GetString().Should().Be("apartment");
        ai.GetProperty("flags").ValueKind.Should().Be(JsonValueKind.Array);
        ai.GetRawText().Should().NotContain("phone").And.NotContain("contacts").And.NotContain("sellerName");
        events[1].Data!.Value.GetProperty("base").GetProperty("tags").GetArrayLength().Should().Be(3);
        Events(await PullAsync(host, late)).Should().BeEmpty("a subscription that never held the post gets no answer for it");

        var stored = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        stored.Revision.Should().Be(1);
        stored.Base.Extras.Keys.Should().NotContain("listingAi", "the answer rides the event; the stored post never holds it");
        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_extractions WHERE post_id=@post AND use_case='extract-listing'",
            ("post", saved.Post.Id))).Should().Be("done");
    }

    [Fact]
    public async Task Answer_ShouldDeliverOnceAPromptVersionAndModel_AndAgainForAnotherModel()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var subscription = await SubscribeAsync(host, pipeline);
        var saved = await olx.SaveAsync(pipeline, "IDrrun", Title, Text);
        var extraction = PostExtractionTests.Extraction(host);

        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.ListingAnswer()), ScriptedAiModelCaller.Answer(PostExtractionTests.ListingAnswer()));
        PostHost.Value(await extraction.RunAsync(saved.Post.Id));
        PostHost.Value(await extraction.RunAsync(saved.Post.Id));
        Events(await PullAsync(host, subscription, 2)).Select(item => item.Type).Should().Equal("post.new", "post.extracted");

        host.Scoped.GetRequiredService<IOptions<PostExtractionOptions>>().Value.Models = ["anthropic/claude-sonnet-5-5"];
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.ListingAnswer()));
        PostHost.Value(await extraction.RunAsync(saved.Post.Id));

        var events = Events(await PullAsync(host, subscription, 3));
        events.Select(item => item.Type).Should().Equal("post.new", "post.extracted", "post.extracted");
        events.Skip(1).Select(item => item.Revision).Should().OnlyContain(revision => revision == 1);
        events.Skip(1).Select(item => item.Data!.Value.GetProperty("base").GetProperty("extras").GetProperty("listingAi").GetProperty("model").GetString())
            .Should().Equal("anthropic/claude-haiku-5-5", "anthropic/claude-sonnet-5-5");
        events.Select(item => item.ItemId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Answer_ShouldDeliverNothing_WhenTheModelFails()
    {
        await using var host = await StartAsync();
        var olx = await OlxAsync(host);
        var pipeline = await olx.PipelineAsync(ApartmentsUrl);
        var subscription = await SubscribeAsync(host, pipeline);
        var saved = await olx.SaveAsync(pipeline, "IDfail", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Error(AiErrorKind.RateLimited));

        (await PostExtractionTests.Extraction(host).ProcessDueAsync()).Should().Be(1);

        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_extractions WHERE post_id=@post", ("post", saved.Post.Id))).Should().Be("failed");
        Events(await PullAsync(host, subscription, 1)).Select(item => item.Type).Should().Equal("post.new");
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id)).Revision.Should().Be(1);
    }

    private static async Task<TagsHost> StartAsync(bool enabled = true, PostExtractionWant[]? wanted = null)
    {
        var host = await TagsHost.StartAsync(enabled, configure: services =>
        {
            services.AddHarvestPostExtraction().Configure(options => options.Wanted = [.. wanted ?? [PostExtractionTests.Want("listing", "real-estate")]]);
            services.AddSingleton<ISourceAdapter>(new OlxCatalog());
            services.AddSingleton<IDeliveryPostFilter>(new QuietListingFilter());
        });
        var prompts = host.Scoped.GetRequiredService<IPromptsBroker>();
        foreach (var useCase in PostExtractionConstants.UseCases)
        {
            (await prompts.PutUseCaseAsync(new UseCaseModel
            {
                Key = useCase, PromptMode = PromptMode.Versioned, OutputSchema = PostExtractionDefinitions.OutputSchema(useCase), KeepOutputs = true,
            })).ValueOrThrow();
            (await prompts.SavePromptAsync(new PromptDraft { UseCase = useCase, SystemTemplate = "Extract.", UserTemplate = "{{title}}\n{{text}}", By = "test" }))
                .ValueOrThrow();
        }
        return host;
    }

    private static async Task<Olx> OlxAsync(TagsHost host, ModelProcessingMode mode = ModelProcessingMode.Auto)
    {
        var source = PostHost.Value(await host.Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = host.ContainerId, Family = SourceFamily.Site, Handle = "olx.uz", Coverage = SourceCoverage.Filtered,
            Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch, Authority = 50, CheckPolicy = "web-direct",
            ModelProcessing = mode,
        }));
        return new Olx(host, source.Id);
    }

    private static async Task<Guid> SubscribeAsync(TagsHost host, Guid pipeline, BackfillMode backfill = BackfillMode.Live, string? filterKey = null) =>
        PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = pipeline, Backfill = backfill, FilterKey = filterKey,
        })).Id;

    /// <summary>Pulls a subscription's page, waiting up to five seconds for <paramref name="expected"/> events: a pull reads below the
    /// cluster's oldest open transaction, so a concurrent test can briefly hide a committed event.</summary>
    private static async Task<CursorPage<DeliveryEvent>> PullAsync(TagsHost host, Guid subscription, int expected = 0)
    {
        for (var attempt = 1; ; attempt++)
        {
            var page = PostHost.Value(await host.Delivery.PullAsync(subscription));
            if (page.Data.Count >= expected || attempt == 20)
                return page;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private static IReadOnlyList<DeliveryEvent> Events(CursorPage<DeliveryEvent> page) => page.Data;

    private static Task<long> WorkAsync(TagsHost host, Guid postId, string kind) =>
        host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind", ("post", postId), ("kind", kind));

    /// <summary>One OLX source in the test container: its pipelines are the sections, and a save is a capture of a run on one.</summary>
    private sealed record Olx(TagsHost Host, Guid SourceId)
    {
        internal async Task<Guid> PipelineAsync(string searchUrl) =>
            (await Host.Registry.DeclarePipelineAsync(new PipelineDeclaration
            {
                ContainerId = Host.ContainerId, SourceId = SourceId, Scope = new PipelineScope { Url = searchUrl }, Name = "OLX · " + searchUrl[^20..],
            })).ValueOrThrow().Id;

        internal async Task<CaptureSaveResult> SaveAsync(Guid pipeline, string key, string title, string text)
        {
            var run = Guid.CreateVersion7(Host.Clock.GetUtcNow());
            await Host.ScalarAsync<long>("""
                WITH run AS (
                    INSERT INTO harvest_runs(id,container_id,pipeline_id,kind,trigger,scheduled_for,status,queued_at,started_at)
                    SELECT @run,container_id,id,'capture','manual',@now,'completed',@now,@now FROM harvest_pipelines WHERE id=@pipeline RETURNING 1)
                SELECT count(*) FROM run
                """, ("run", run), ("now", Host.Clock.GetUtcNow()), ("pipeline", pipeline));
            string url = $"https://www.olx.uz/d/obyavlenie/synthetic-{key}.html";
            var capture = new Capture
            {
                ExternalKey = key, Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = Host.Clock.GetUtcNow(),
                Body = Encoding.UTF8.GetBytes($"{title}\n{text}"), ContentProjection = $"{title}\n{text}", HttpStatus = 200, ContentType = "text/html",
            };
            var envelope = new PostEnvelope
            {
                Kind = PostKind.Listing, Title = title, Text = text, Language = new PostLanguage { Code = "uz" },
                Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
            };
            return (await Host.Posts.SaveCaptureAsync(Host.ContainerId, SourceId, run, capture, envelope)).ValueOrThrow();
        }
    }

    /// <summary>Stands in for <c>marketplace-listing-v1</c>: it passes listings and asks to go without tag-only revisions.</summary>
    private sealed class QuietListingFilter : IDeliveryPostFilter
    {
        internal const string FilterKey = "quiet-listing-v1";

        public string Key => FilterKey;

        public bool Matches(PostEnvelope envelope) => envelope.Kind == PostKind.Listing;

        public bool IgnoresTagOnlyChange => true;
    }

    /// <summary>Stands in for the OLX adapter: it serves <c>olx.uz</c> and declares four sections, two of which imply tags, one
    /// implies none, and one names a taxonomy node that does not exist.</summary>
    private sealed class OlxCatalog : ISourceAdapter, ISourceSectionCatalog
    {
        public string Key => "olx-uz";

        public SourceFamily Family => SourceFamily.Site;

        public bool Serves(SourceModel source) => source.Family == Family && source.Handle == "olx.uz";

        public AdapterDescriptor Describe(SourceModel source) => new()
        {
            SourceUrl = "https://www.olx.uz/", HostKey = "www.olx.uz", Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>(),
        };

        public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) =>
        [
            Section("apartments-rent", ApartmentsUrl, new SectionImpliedTags("listing", "real-estate/apartments")),
            Section("houses-rent", HousesUrl, new SectionImpliedTags("listing", "real-estate/houses")),
            Section("other", OtherUrl, null),
            Section("ghost", GhostUrl, new SectionImpliedTags("listing", "real-estate/no-such-node")),
        ];

        private static SourceSectionDefinition Section(string key, string url, SectionImpliedTags? implied) =>
            new(key, url, "und") { Key = key, RestrictToPath = false, RegisterPassivePipeline = false, ImpliedTags = implied };
    }
}
