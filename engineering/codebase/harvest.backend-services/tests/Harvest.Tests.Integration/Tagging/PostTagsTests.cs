using System.Globalization;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Tagging;

/// <summary>The post-tags pass over PostgreSQL with the real AI runner and a scripted caller: queued on write, written back
/// as a revision with <c>post.changed</c>, never re-queued by its own revision.</summary>
public sealed class PostTagsTests
{
    private const string WorkCount = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind='post-tags'";

    [Fact]
    public async Task Write_ShouldQueueTagging_AndTheAnswerShouldBecomeARevisionThatQueuesNothing()
    {
        await using var host = await TagsHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, FilterKey = "tag:domain:style-fashion",
        }));
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi dekabrda bo'ladi.");
        (await host.ScalarAsync<long>(WorkCount, ("post", saved.Post.Id))).Should().Be(1);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(
            domains: [new { node = "style-fashion/womens-fashion", primary = true }])));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        post.Revision.Should().Be(2);
        post.Base.Tags.Should().OnlyContain(tag => tag.Tier == ExtractionTier.SmallModel && tag.Version == "post-tags@1");
        post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:event", "domain:style-fashion/womens-fashion", "domain:style-fashion",
            "topic:fashion-week");
        post.Base.Extras[PostTagsConstants.ExtrasNamespace].GetProperty("titleEn").GetString().Should().Be("Tashkent Fashion Week");
        (await host.ScalarAsync<string[]>("SELECT changed FROM harvest_post_revisions WHERE post_id=@post AND revision=2", ("post", post.Id)))
            .Should().Equal("extras", "tags");
        (await host.ScalarAsync<string>("""
            SELECT string_agg(type || ':' || revision, ',' ORDER BY event_id) FROM harvest_subscription_event WHERE subscription_id=@subscription
            """, ("subscription", subscription.Id))).Should().Be("post.changed:2", "only the tagged revision passes the domain tag filter");
        (await host.ScalarAsync<string>("""
            SELECT status || '|' || entity_type || '|' || model || '|' || prompt_version || '|' || (inference_id IS NOT NULL) || '|' || revision
            FROM harvest_post_tags WHERE post_id=@post
            """, ("post", post.Id))).Should().Be("done|event|anthropic/claude-haiku-5-5|1|true|1");
        (await host.ScalarAsync<long>(WorkCount, ("post", post.Id))).Should().Be(0, "the tag-only revision kept its content hash");
        (await host.Tags.ProcessDueAsync()).Should().Be(0);

        var call = host.Caller.Requests.Should().ContainSingle().Subject;
        call.System.Should().Contain("\nstyle-fashion/womens-fashion — Women's Fashion\n").And.NotContain("\nevents —")
            .And.NotContain("\ngenres —").And.NotContain("\nsensitive-topics —");
        string seen = post.FirstSeenAtUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        call.User.Should().StartWith($"Tashkent Fashion Week\nunknown\nnone\nunknown\nunknown; first seen {seen}\nen\nModa haftaligi",
            "an unknown publication date reads the post's first capture");

        host.Clock.Advance(TimeSpan.FromHours(1));
        var edited = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi yanvarga ko'chdi.");
        edited.Post.Revision.Should().Be(3);
        edited.Post.Base.Tags.Should().HaveCount(4, "the last answer stays on the post until a new one replaces it");
        (await host.ScalarAsync<long>(WorkCount, ("post", post.Id))).Should().Be(1, "a new text queues tagging again");
    }

    [Fact]
    public async Task Write_ShouldQueueNothing_WhenTaggingOrAiIsOffOrTheSourceDisallowsAi()
    {
        await using (var off = await TagsHost.StartAsync(enabled: false))
        {
            var saved = await off.SaveAsync("off", "A fair", "A textile fair.");
            (await off.ScalarAsync<long>(WorkCount, ("post", saved.Post.Id))).Should().Be(0);
        }
        await using (var noAi = await TagsHost.StartAsync(ai: false))
        {
            var saved = await noAi.SaveAsync("no-ai", "A fair", "A textile fair.");
            (await noAi.ScalarAsync<long>(WorkCount, ("post", saved.Post.Id))).Should().Be(0);
            (await noAi.Tags.RunAsync(saved.Post.Id)).Should().BeOfType<Result<PostTagsRunModel>.Failure>();
        }
        await using var host = await TagsHost.StartAsync();
        PostHost.Value(await host.Registry.UpdateSourceAsync(host.SourceId, new SourceUpdate { AiAllowed = false }));
        var closed = await host.SaveAsync("closed", "A fair", "A textile fair.");
        (await host.ScalarAsync<long>(WorkCount, ("post", closed.Post.Id))).Should().Be(0);
        (await host.Tags.RunAsync(closed.Post.Id)).Should().BeOfType<Result<PostTagsRunModel>.Failure>()
            .Which.Error.Type.Should().Be(WoW.Two.Sdk.Beta.Be.Foundation.Errors.AppErrorType.Forbidden);
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest())).ValueOrThrow().Should().Be(0);
        host.Caller.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_ShouldStoreTheFailureAndWriteNoTags_WhenTheModelFails()
    {
        await using var host = await TagsHost.StartAsync();
        var saved = await host.SaveAsync("fail", "Tashkent Fashion Week", "Moda haftaligi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Error(AiErrorKind.RateLimited));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        post.Revision.Should().Be(1);
        post.Base.Tags.Should().BeEmpty();
        (await host.ScalarAsync<string>("SELECT status || '|' || error || '|' || (entity_type IS NULL) FROM harvest_post_tags WHERE post_id=@post",
            ("post", post.Id))).Should().Be("failed|rate-limited|true");
        (await host.ScalarAsync<string>("""
            SELECT last_error || '|' || attempts || '|' || (not_before > @now) || '|' || (lease_owner IS NULL)
            FROM harvest_post_work WHERE post_id=@post
            """, ("post", post.Id), ("now", host.Clock.GetUtcNow()))).Should().Be("rate-limited|1|true|true");
        (await host.Tags.ProcessDueAsync()).Should().Be(0, "the retry waits for its delay");

        host.Clock.Advance(TimeSpan.FromMinutes(16));
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_tags WHERE post_id=@post", ("post", post.Id))).Should().Be("done");
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, post.Id)).Revision.Should().Be(2);
    }

    [Fact]
    public async Task Process_ShouldStoreUnavailableWithoutACall_WhenNoPromptIsRegistered()
    {
        await using var host = await TagsHost.StartAsync(register: false);
        var saved = await host.SaveAsync("unregistered", "Tashkent Fashion Week", "Moda haftaligi.");

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        host.Caller.Requests.Should().BeEmpty();
        (await host.ScalarAsync<string>("SELECT status || '|' || error FROM harvest_post_tags WHERE post_id=@post", ("post", saved.Post.Id)))
            .Should().Be("unavailable|use-case-missing");
        (await host.ScalarAsync<long>(WorkCount, ("post", saved.Post.Id))).Should().Be(1, "it retries once a prompt exists");
    }

    [Fact]
    public async Task Process_ShouldFlagAnUnknownNode_AndStoreOnlyTheKnownOnes()
    {
        await using var host = await TagsHost.StartAsync();
        var saved = await host.SaveAsync("textile", "Textile expo", "Toʻqimachilik koʻrgazmasi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(
            domains: [new { node = "style-fashion/textiles", primary = true }, new { node = "business-and-finance", primary = false }],
            topics: ["textile", "текстиль"])));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        (await host.ScalarAsync<string>("SELECT domains::text FROM harvest_post_tags WHERE post_id=@post", ("post", saved.Post.Id)))
            .Should().Be("""[{"node": "business-and-finance", "primary": true}]""");
        (await host.ScalarAsync<string>("""
            SELECT string_agg(f->>'field' || ':' || COALESCE(f->>'proposed',''), ',' ORDER BY f->>'field')
            FROM harvest_post_tags t, jsonb_array_elements(t.flags) f WHERE t.post_id=@post
            """, ("post", saved.Post.Id))).Should().Be("domains:style-fashion/textiles,topics:текстиль");
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id)).Base.Tags.Select(tag => tag.Tag)
            .Should().Equal("type:event", "domain:business-and-finance", "topic:textile");

        var report = (await host.Tags.ReportAsync()).ValueOrThrow();
        report.Statuses.Should().Equal(new Dictionary<string, long> { ["done"] = 1 });
        report.Domains.Should().Equal(new Dictionary<string, long> { ["business-and-finance"] = 1 });
        report.Flags.Should().ContainEquivalentOf(new PostTagsFlagCount { Field = "domains", Proposed = "style-fashion/textiles", Count = 1 });
        report.Attempts.Should().Be(1);
        report.P95LatencyMs.Should().Be(50);
    }

    [Fact]
    public async Task Run_ShouldPrintTheAnswerAndWriteNothing_WhenItIsADryRun()
    {
        await using var host = await TagsHost.StartAsync(enabled: false);
        var saved = await host.SaveAsync("dry", "Tashkent Fashion Week", "Moda haftaligi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));

        var dry = (await host.Tags.RunAsync(saved.Post.Id, dryRun: true)).ValueOrThrow();

        dry.Status.Should().Be("done");
        dry.Stored.Should().BeFalse();
        dry.Answer!.Value.GetProperty("entityType").GetString().Should().Be("event");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_tags")).Should().Be(0);
        PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id)).Revision.Should().Be(1);

        var run = (await host.Tags.RunAsync(saved.Post.Id)).ValueOrThrow();
        run.Stored.Should().BeTrue();
        run.NewRevision.Should().Be(2);
        run.FromCache.Should().BeTrue("the dry run's answer is stored in the inference log for the same prompt and input");
        host.Caller.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Backfill_ShouldQueueStoredPosts_AndSkipThoseTaggedForTheirCurrentText()
    {
        await using var host = await TagsHost.StartAsync(enabled: false);
        var tagged = await host.SaveAsync("tagged", "Tashkent Fashion Week", "Moda haftaligi.");
        var untagged = await host.SaveAsync("untagged", "Book fair", "Kitob yarmarkasi.");
        var other = await host.SourceAsync("other.uz");
        var elsewhere = await host.SaveAsync("elsewhere", "Car show", "Avtosalon.", other, "other.uz");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work")).Should().Be(0);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.RunAsync(tagged.Post.Id)).ValueOrThrow().Status.Should().Be("done");

        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest { SourceId = host.SourceId })).ValueOrThrow().Should().Be(1);
        (await host.ScalarAsync<long>(WorkCount, ("post", untagged.Post.Id))).Should().Be(1);
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest { Limit = 5 })).ValueOrThrow().Should().Be(2,
            "the waiting post is re-armed and the other source's post joins; the tagged one's SQL hash matches its answer");
        (await host.ScalarAsync<long>(WorkCount, ("post", elsewhere.Post.Id))).Should().Be(1);
        (await host.ScalarAsync<long>(WorkCount, ("post", tagged.Post.Id))).Should().Be(0);
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest { SinceUtc = host.Clock.GetUtcNow().AddMinutes(1) })).ValueOrThrow()
            .Should().Be(0);
    }

    [Fact]
    public async Task EvalSubject_ShouldResolveThePostsVariables_ForItsCurrentRevision()
    {
        await using var host = await TagsHost.StartAsync();
        var saved = await host.SaveAsync("eval", "Tashkent Fashion Week", "Moda haftaligi.");
        var source = host.Scoped.GetServices<IEvalSubjectSource>().Should().ContainSingle().Subject;

        var variables = await source.ResolveAsync(PostTagsConstants.UseCase, $"harvest:post:{saved.Post.Id:D}@1");

        source.Prefix.Should().Be("harvest:post:");
        variables!.Keys.Should().BeEquivalentTo(PostTagsDefinition.Variables);
        variables["title"].Should().Be("Tashkent Fashion Week");
        variables["taxonomy"].Should().Contain("style-fashion — Style & Fashion");
        (await source.ResolveAsync("olx-rental-terms", $"harvest:post:{saved.Post.Id:D}@1")).Should().BeNull();
        (await source.ResolveAsync(PostTagsConstants.UseCase, $"harvest:post:{saved.Post.Id:D}@7")).Should().BeNull();
    }
}
