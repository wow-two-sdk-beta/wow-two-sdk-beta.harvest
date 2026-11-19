using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.Entities;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.PostExtraction;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;
using WoW.Two.Sdk.Beta.Be.Ai.Prices;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Reprocessing;

/// <summary>The operator controls of the AI passes over PostgreSQL with the real AI module and a scripted caller: source
/// modes in the workers, re-runs by scope and filter, dry runs with a cost estimate, the downstream chain a re-run sets off,
/// the eval subject source for extraction, and eval sets from stored posts. No live model call.</summary>
public sealed class ReprocessingTests
{
    private const string Title = "Tashkent Fashion Week";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Uzexpocentre'da bo'ladi. Kirish 50 000 so'm. "
        + "Dizaynerlar uchun ro'yxatdan o'tish 1 dekabrgacha davom etadi.";
    private const string ExtractUser = "{{title}}\n{{text}}\n{{dates}}\n{{prices}}\n{{entityType}}\n{{domains}}\n{{timeframe}}\n{{titleEn}}";
    private const string WorkCount = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind";

    [Fact]
    public async Task Workers_ShouldSkipManualAndOffSources_WhileANamedRunStillServesManual()
    {
        await using var host = await StartAsync();
        var queued = await host.SaveAsync("queued", Title, Text);
        (await WorkAsync(host, queued.Post.Id, "post-tags")).Should().Be(1, "the source is automatic");
        await ModeAsync(host, host.SourceId, ModelProcessingMode.Manual);

        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await WorkAsync(host, queued.Post.Id, "post-tags")).Should().Be(0, "the worker reads the mode now and drops the row");
        var later = await host.SaveAsync("later", "A fair", "A textile fair.");
        (await WorkAsync(host, later.Post.Id, "post-tags")).Should().Be(0, "a manual source queues nothing on a write");
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest())).ValueOrThrow().Should().Be(0);
        host.Caller.Requests.Should().BeEmpty();

        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.RunAsync(later.Post.Id)).ValueOrThrow().Status.Should().Be("done", "an operator names the post");

        await ModeAsync(host, host.SourceId, ModelProcessingMode.Off);
        (await host.Tags.RunAsync(later.Post.Id)).Should().BeOfType<Result<PostTagsRunModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Forbidden);
        await host.ExecuteAsync("""
            INSERT INTO harvest_post_work(post_id,kind,container_id,content_hash,queued_at,not_before,explicit)
            SELECT id,'post-tags',container_id,sha256('x'::bytea),@now,@now,true FROM harvest_posts WHERE id=@post
            """, ("post", queued.Post.Id), ("now", host.Clock.GetUtcNow()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await WorkAsync(host, queued.Post.Id, "post-tags")).Should().Be(0, "even a named row never runs under off");
        host.Caller.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Rerun_ShouldRunNamedPostsAndListedSourcesUnderManual_RefuseOff_AndLeaveManualOutOfAllSources()
    {
        await using var host = await StartAsync();
        var manualSource = await host.SourceAsync("manual.example");
        var offSource = await host.SourceAsync("off.example");
        await ModeAsync(host, manualSource, ModelProcessingMode.Manual);
        await ModeAsync(host, offSource, ModelProcessingMode.Off);
        var manual = await host.SaveAsync("m", Title, Text, manualSource, "manual.example");
        var off = await host.SaveAsync("o", "Bridal show", "Kelinlik liboslari ko'rgazmasi.", offSource, "off.example");

        var named = (await Reruns(host).RerunAsync(new PostRerunRequest
        {
            PostIds = [manual.Post.Id, off.Post.Id, Guid.NewGuid()], UseCases = [PostTagsConstants.UseCase], RequestedBy = "test",
        })).ValueOrThrow();

        named.Scope.Should().Be("posts");
        named.RunId.Should().NotBeNull();
        named.Queued.Should().Be(1);
        named.Refused.Should().Be(1);
        named.ByUseCase[PostTagsConstants.UseCase].NotEligible.Should().Be(1, "the third post does not exist");
        named.Skipped.Should().Be(1);
        (await host.ScalarAsync<string>("SELECT run_id::text || '|' || explicit FROM harvest_post_work WHERE post_id=@post",
            ("post", manual.Post.Id))).Should().Be($"{named.RunId:D}|true");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_tags WHERE post_id=@post", ("post", manual.Post.Id))).Should().Be("done");

        var bySource = (await Reruns(host).RerunAsync(new PostRerunRequest
        {
            SourceIds = [manualSource, offSource], UseCases = [PostTagsConstants.UseCase],
        })).ValueOrThrow();
        bySource.Scope.Should().Be("sources");
        bySource.Queued.Should().Be(1, "listing a manual source is an explicit choice, like naming its posts");
        bySource.ByUseCase[PostTagsConstants.UseCase].Manual.Should().Be(0);
        bySource.Refused.Should().Be(1, "an off source is refused whatever the scope");
        (await host.ScalarAsync<string>("SELECT run_id::text || '|' || explicit FROM harvest_post_work WHERE post_id=@post",
            ("post", manual.Post.Id))).Should().Be($"{bySource.RunId:D}|true");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await WorkAsync(host, manual.Post.Id, "post-tags")).Should().Be(0, "the worker runs the listed source's explicit row");
        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_tags WHERE post_id=@post", ("post", manual.Post.Id))).Should().Be("done");
        var everything = (await Reruns(host).RerunAsync(new PostRerunRequest { AllSources = true, UseCases = [PostTagsConstants.UseCase] }))
            .ValueOrThrow();
        everything.Scope.Should().Be("all");
        everything.Queued.Should().Be(0);
        everything.ByUseCase[PostTagsConstants.UseCase].Manual.Should().Be(1, "only a run over every source leaves manual out");
        everything.Skipped.Should().Be(1);
        everything.Refused.Should().Be(1);
    }

    [Fact]
    public async Task Rerun_ShouldKeepToOlderPromptVersionsAndTheSinceFilter()
    {
        await using var host = await StartAsync();
        var first = await host.SaveAsync("first", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        await host.Tags.ProcessDueAsync();
        host.Clock.Advance(TimeSpan.FromHours(1));
        var cutoff = host.Clock.GetUtcNow();
        var second = await host.SaveAsync("second", "Textile expo", "To'qimachilik ko'rgazmasi dekabrda.");
        var third = await host.SaveAsync("third", "Bridal show", "Kelinlik liboslari ko'rgazmasi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        await host.Tags.ProcessDueAsync();
        await SavePromptAsync(host, PostTagsConstants.UseCase, "Version 2. " + TagsHost.SystemTemplate, TagsHost.UserTemplate);

        var renewed = (await Reruns(host).RerunAsync(new PostRerunRequest { PostIds = [third.Post.Id] })).ValueOrThrow();
        renewed.UseCases.Should().Equal("post-tags", "extract-event", "extract-listing");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(topics: ["bridal"])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<int>("SELECT prompt_version FROM harvest_post_tags WHERE post_id=@post", ("post", third.Post.Id))).Should().Be(2);

        var older = (await Reruns(host).RerunAsync(new PostRerunRequest
        {
            AllSources = true, UseCases = [PostTagsConstants.UseCase], PromptBefore = 2, DryRun = true,
        })).ValueOrThrow();
        older.Queued.Should().Be(2, "the third post's answer already comes from version 2");
        var recent = (await Reruns(host).RerunAsync(new PostRerunRequest
        {
            SourceIds = [host.SourceId], UseCases = [PostTagsConstants.UseCase], PromptBefore = 2, SinceUtc = cutoff, DryRun = true,
        })).ValueOrThrow();
        recent.Queued.Should().Be(1, "only the second post changed since the cutoff and holds a version 1 answer");
        var none = (await Reruns(host).RerunAsync(new PostRerunRequest
        {
            PostIds = [first.Post.Id, second.Post.Id], UseCases = [PostExtractionConstants.EventUseCase], DryRun = true,
        })).ValueOrThrow();
        none.ByUseCase[PostExtractionConstants.EventUseCase].Queued.Should().Be(2, "a named post with event tags extracts whatever the demand");
    }

    [Fact]
    public async Task DryRun_ShouldCountAndEstimateTheCostWithoutWritingAnything()
    {
        await using var host = await StartAsync();
        (await host.Scoped.GetRequiredService<IModelPricesBroker>().PutAsync(new ModelPriceModel
        {
            Provider = "anthropic", Model = "claude-haiku-5-5", EffectiveFromUtc = host.Clock.GetUtcNow().AddDays(-1),
            InputPerMTokMicroUsd = 1_000_000, OutputPerMTokMicroUsd = 5_000_000,
        })).ValueOrThrow();
        await host.SaveAsync("one", Title, Text);
        await host.SaveAsync("two", "Textile expo", "To'qimachilik ko'rgazmasi dekabrda.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(2);

        var dry = (await Reruns(host).RerunAsync(new PostRerunRequest
        {
            AllSources = true, UseCases = [PostTagsConstants.UseCase], DryRun = true,
        })).ValueOrThrow();

        dry.DryRun.Should().BeTrue();
        dry.RunId.Should().BeNull();
        dry.Queued.Should().Be(2);
        var tags = dry.ByUseCase[PostTagsConstants.UseCase];
        tags.Model.Should().Be("anthropic/claude-haiku-5-5");
        tags.CostBasis.Should().Be(2);
        tags.CostPerPostMicroUsd.Should().Be(200, "100 input tokens at $1 and 20 output tokens at $5 per million");
        tags.EstimatedCostMicroUsd.Should().Be(400);
        dry.EstimatedCostMicroUsd.Should().Be(400);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work")).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_processing_runs")).Should().Be(0);
        host.Caller.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rerun_ShouldReplaceTheTags_ThenTheClassificationExtractionAndEntitiesShouldFollow()
    {
        await using var host = await StartAsync(entities: true, PostExtractionTests.Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer()));
        await host.Tags.ProcessDueAsync();
        (await Extraction(host).ProcessDueAsync()).Should().Be(1);
        (await Entities(host).ProcessDueAsync()).Should().Be(1);
        var before = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        before.Base.Classification!.Kind.Should().Be("runway");
        await SavePromptAsync(host, PostTagsConstants.UseCase, "Version 2. " + TagsHost.SystemTemplate, TagsHost.UserTemplate);

        var run = (await Reruns(host).RerunAsync(new PostRerunRequest { PostIds = [saved.Post.Id], UseCases = [PostTagsConstants.UseCase] }))
            .ValueOrThrow();
        var runId = run.RunId!.Value;
        var waiting = (await Reruns(host).GetRunAsync(runId)).ValueOrThrow();
        waiting.Done.Should().BeFalse();
        waiting.Remaining["post-tags"].Waiting.Should().Be(1);
        waiting.Requested["post-tags"].Queued.Should().Be(1);

        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/womens-fashion", primary = true }])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT prompt_version || '|' || (domains->0->>'node') FROM harvest_post_tags WHERE post_id=@post",
            ("post", saved.Post.Id))).Should().Be("2|style-fashion/womens-fashion", "the done answer for the same content is replaced");
        var retagged = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        retagged.Revision.Should().BeGreaterThan(before.Revision);
        retagged.Base.Tags.Select(tag => tag.Tag).Should().Contain("domain:style-fashion/womens-fashion");
        retagged.Base.Classification!.Classifier.Should().EndWith("@post-tags:2");
        (await host.ScalarAsync<string>("SELECT run_id::text FROM harvest_post_work WHERE post_id=@post AND kind='extract-event'",
            ("post", saved.Post.Id))).Should().Be(runId.ToString("D"), "the extraction follows the changed tags, over its done answer");

        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer().Replace("\"runway\"", "\"festival\"", StringComparison.Ordinal)));
        (await Extraction(host).ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT answer->>'format' FROM harvest_post_extractions WHERE post_id=@post", ("post", saved.Post.Id)))
            .Should().Be("festival");
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id)).Base.Classification!.Kind.Should().Be("festival");
        (await WorkAsync(host, saved.Post.Id, EntityConstants.WorkKind)).Should().Be(1);
        (await Reruns(host).GetRunAsync(runId)).ValueOrThrow().Remaining.Keys.Should().Equal(EntityConstants.WorkKind);

        (await Entities(host).ProcessDueAsync()).Should().Be(1);
        var done = (await Reruns(host).GetRunAsync(runId)).ValueOrThrow();
        done.Done.Should().BeTrue();
        done.Remaining.Should().BeEmpty();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_entity_relations WHERE post_id=@post", ("post", saved.Post.Id))).Should().Be(1);
    }

    [Fact]
    public async Task Rerun_ShouldRefuseMalformedRequests_AndTaggingOff()
    {
        await using var host = await StartAsync();
        var reruns = Reruns(host);
        foreach (var request in new[]
                 {
                     new PostRerunRequest(),
                     new PostRerunRequest { AllSources = true, SourceIds = [host.SourceId] },
                     new PostRerunRequest { AllSources = true, UseCases = ["rental-terms"] },
                     new PostRerunRequest { AllSources = true, PromptBefore = 0 },
                     new PostRerunRequest { PostIds = [.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())] },
                 })
            (await reruns.RerunAsync(request)).Should().BeOfType<Result<PostRerunModel>.Failure>()
                .Which.Error.Type.Should().Be(AppErrorType.Validation);
        (await reruns.GetRunAsync(Guid.NewGuid())).Should().BeOfType<Result<PostRerunStatusModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.NotFound);

        await using var off = await StartAsync(enabled: false);
        (await Reruns(off).RerunAsync(new PostRerunRequest { AllSources = true })).Should().BeOfType<Result<PostRerunModel>.Failure>()
            .Which.Error.Message.Should().Contain("Tagging is off");
        (await Reruns(off).RerunAsync(new PostRerunRequest { AllSources = true, UseCases = [PostExtractionConstants.EventUseCase] }))
            .Should().BeOfType<Result<PostRerunModel>.Success>("an extraction re-run needs no tagging");
    }

    [Fact]
    public async Task SubjectSource_ShouldResolveTheExtractionInputsExactlyAsThePassBuildsThem()
    {
        await using var host = await StartAsync(entities: false, PostExtractionTests.Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer()));
        await host.Tags.ProcessDueAsync();
        await Extraction(host).ProcessDueAsync();
        var sent = host.Caller.Requests.Last().User;
        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        var source = host.Scoped.GetServices<IEvalSubjectSource>().Single(candidate => candidate.Prefix == "harvest:post:");

        var resolved = await source.ResolveAsync(PostExtractionConstants.EventUseCase, $"harvest:post:{saved.Post.Id:D}@{post.Revision}");

        resolved.Should().NotBeNull();
        Render(ExtractUser, resolved!).Should().Be(sent);
        resolved!["entityType"].Should().Be("event");
        resolved.Keys.Should().BeEquivalentTo(PostExtractionDefinitions.Variables);
        (await source.ResolveAsync(PostExtractionConstants.ListingUseCase, $"harvest:post:{saved.Post.Id:D}"))
            .Should().BeNull("the post is tagged an event");
        (await source.ResolveAsync(PostTagsConstants.UseCase, $"harvest:post:{saved.Post.Id:D}"))!["title"].Should().Be(Title);

        await ModeAsync(host, host.SourceId, ModelProcessingMode.Manual);
        (await source.ResolveAsync(PostExtractionConstants.EventUseCase, $"harvest:post:{saved.Post.Id:D}")).Should().NotBeNull();
        await ModeAsync(host, host.SourceId, ModelProcessingMode.Off);
        (await source.ResolveAsync(PostExtractionConstants.EventUseCase, $"harvest:post:{saved.Post.Id:D}")).Should().BeNull();
    }

    [Fact]
    public async Task EvalSet_ShouldSampleStoredPostsWithUnreviewedExpectedAnswers()
    {
        await using var host = await StartAsync();
        await host.SaveAsync("one", Title, Text);
        await host.SaveAsync("two", "Textile expo", "To'qimachilik ko'rgazmasi dekabrda.");
        await host.SaveAsync("three", "Bridal show", "Kelinlik liboslari ko'rgazmasi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(TagsHost.Answer()),
            ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(3);
        var request = new PostEvalSetRequest
        {
            Set = "fashion-tags", UseCase = PostTagsConstants.UseCase, Sample = 2, Seed = 7, Expected = "current", ScoreFields = ["entityType"],
        };

        var built = (await Reruns(host).BuildEvalSetAsync(request)).ValueOrThrow();

        built.Created.Should().BeTrue();
        built.Sampled.Should().Be(2);
        built.Added.Should().Be(2);
        var evals = host.Scoped.GetRequiredService<IEvalsBroker>();
        (await evals.GetSetAsync("fashion-tags")).ValueOrThrow().ScoreFields.Should().Equal("entityType");
        var items = (await evals.ListItemsAsync("fashion-tags")).ValueOrThrow();
        items.Should().HaveCount(2).And.OnlyContain(item => item.Tags.Contains("unreviewed") && item.LabelledBy == "harvest:current-answer"
            && item.Subject!.StartsWith("harvest:post:", StringComparison.Ordinal) && item.Input.ContainsKey("taxonomy"));
        using (var expected = JsonDocument.Parse(items[0].Expected))
        {
            expected.RootElement.GetProperty("entityType").GetString().Should().Be("event");
            expected.RootElement.GetProperty("confidence").GetRawText().Should().Be("0.9", "the model writes 0.9, never 0.900");
        }

        var again = (await Reruns(host).BuildEvalSetAsync(request)).ValueOrThrow();
        again.Added.Should().Be(0, "the same seed draws the same posts, already in the set");
        again.Skipped.Should().Be(2);
        var rest = (await Reruns(host).BuildEvalSetAsync(request with { Sample = 5, Expected = "empty" })).ValueOrThrow();
        rest.Added.Should().Be(1);
        (await evals.ListItemsAsync("fashion-tags")).ValueOrThrow().Single(item => item.Id == rest.ItemIds[0]).Expected.Should().Be("{}");
        (await Reruns(host).BuildEvalSetAsync(request with { UseCase = PostExtractionConstants.ListingUseCase }))
            .Should().BeOfType<Result<PostEvalSetModel>.Failure>().Which.Error.Type.Should().Be(AppErrorType.Conflict);
        (await Reruns(host).BuildEvalSetAsync(request with { Set = "Not A Key" }))
            .Should().BeOfType<Result<PostEvalSetModel>.Failure>().Which.Error.Type.Should().Be(AppErrorType.Validation);
    }

    private static async Task<TagsHost> StartAsync(bool entities = false, params PostExtractionWant[] wanted) =>
        await StartAsync(enabled: true, entities, wanted);

    private static async Task<TagsHost> StartAsync(bool enabled, bool entities = false, params PostExtractionWant[] wanted)
    {
        var host = await TagsHost.StartAsync(enabled, configure: services =>
        {
            services.AddHarvestPostExtraction().Configure(options => options.Wanted = [.. wanted]);
            services.AddHarvestEntities().Configure(options => options.Enabled = entities);
            services.AddHarvestPostReprocessing();
        });
        foreach (var useCase in PostExtractionConstants.UseCases)
        {
            (await host.Scoped.GetRequiredService<IPromptsBroker>().PutUseCaseAsync(new UseCaseModel
            {
                Key = useCase, PromptMode = PromptMode.Versioned, OutputSchema = PostExtractionDefinitions.OutputSchema(useCase), KeepOutputs = true,
            })).ValueOrThrow();
            await SavePromptAsync(host, useCase, "Extract.", ExtractUser);
        }
        return host;
    }

    private static async Task SavePromptAsync(TagsHost host, string useCase, string system, string user) =>
        (await host.Scoped.GetRequiredService<IPromptsBroker>().SavePromptAsync(new PromptDraft
        {
            UseCase = useCase, SystemTemplate = system, UserTemplate = user, By = "test",
        })).ValueOrThrow();

    private static async Task ModeAsync(TagsHost host, Guid sourceId, ModelProcessingMode mode) =>
        PostHost.Value(await host.Registry.UpdateSourceAsync(sourceId, new SourceUpdate { ModelProcessing = mode })).ModelProcessing.Should().Be(mode);

    private static IPostReprocessingBroker Reruns(TagsHost host) => host.Scoped.GetRequiredService<IPostReprocessingBroker>();

    private static IPostExtractionBroker Extraction(TagsHost host) => host.Scoped.GetRequiredService<IPostExtractionBroker>();

    private static IEntityBroker Entities(TagsHost host) => host.Scoped.GetRequiredService<IEntityBroker>();

    private static Task<long> WorkAsync(TagsHost host, Guid postId, string kind) => host.ScalarAsync<long>(WorkCount, ("post", postId), ("kind", kind));

    private static string Render(string template, IReadOnlyDictionary<string, string?> variables) =>
        variables.Aggregate(template, (text, variable) => text.Replace("{{" + variable.Key + "}}", variable.Value, StringComparison.Ordinal));
}
