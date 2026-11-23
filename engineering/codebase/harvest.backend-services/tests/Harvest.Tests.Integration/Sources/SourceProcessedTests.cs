using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Sources;

/// <summary>The processed read over a real tagging pass: the answer's provider, model and prompt version come from its
/// row, its cost and latency from the AI module's inference log.</summary>
public sealed class SourceProcessedTests
{
    [Fact]
    public async Task QuerySourceProcessed_ShouldPriceEachAnswerFromTheInferenceLog()
    {
        await using var host = await TagsHost.StartAsync(configure: services => services.AddHarvestSources());
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi dekabrda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(
            domains: [new { node = "style-fashion/womens-fashion", primary = true }])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        var untagged = await host.SaveAsync("later", "A later post", "Queued, not tagged yet.");

        var overview = host.Scoped.GetRequiredService<ISourceOverviewBroker>();
        var page = PostHost.Value(await overview.QuerySourceProcessedAsync(host.SourceId, new SourceProcessedQuery()));

        page.Items.Select(post => post.PostId).Should().Contain(saved.Post.Id).And.NotContain(untagged.Post.Id);
        var tags = page.Items.Single(post => post.PostId == saved.Post.Id).Tags;
        tags.Should().Match<ProcessedTagsModel>(value => value.Status == "done" && value.Current && value.EntityType == "event"
            && value.TitleEn == "Tashkent Fashion Week");
        tags.Domains.Should().ContainSingle().Which.Should().Be(new ProcessedDomainModel("style-fashion/womens-fashion", "Women's Fashion", true));
        tags.Answer.Should().Match<ProcessedAnswerModel>(answer => answer.Provider == "anthropic" && answer.Model == "claude-haiku-5-5"
            && answer.PromptVersion == 1 && answer.LatencyMs == 50 && answer.CostMicroUsd != null
            && answer.InputTokens == 100 && answer.OutputTokens == 20);
    }

    [Fact]
    public async Task QuerySourceProcessed_ShouldCarryThePostFields_AndCountOnlySuccessfulTags()
    {
        await using var host = await TagsHost.StartAsync(configure: services => services.AddHarvestSources());
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi dekabrda bo'ladi.");
        var duplicate = await host.SaveAsync("dup", "Tashkent Fashion Week again", "Moda haftaligi yana dekabrda.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(2);
        var failed = await host.SaveAsync("failed", "A fair", "The classifier will fail on this post.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Error(AiErrorKind.RateLimited));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_tags WHERE post_id=@post", ("post", failed.Post.Id)))
            .Should().Be("failed");
        await host.SaveAsync("later", "A later post", "Queued, not tagged yet.");
        PostHost.Value(await host.Posts.MergeAsync(host.ContainerId, saved.Post.Id, duplicate.Post.Id));

        var overview = host.Scoped.GetRequiredService<ISourceOverviewBroker>();
        var page = PostHost.Value(await overview.QuerySourceProcessedAsync(host.SourceId, new SourceProcessedQuery()));

        page.Items.Should().ContainSingle().Which.Should().Match<SourceProcessedPostModel>(post => post.PostId == saved.Post.Id
            && post.Kind == PostKind.Event && post.State == PostState.Live && post.Language == "en" && post.PipelineId == null);
        var source = PostHost.Value(await overview.ListOverviewAsync(host.ContainerId)).Single(row => row.SourceId == host.SourceId);
        source.ProcessedCount.Should().Be(page.Total).And.Be(1);
        source.PostCount.Should().Be(3);
        (await overview.GetSourceProcessedPostAsync(host.SourceId, failed.Post.Id))
            .Should().BeOfType<Result<SourceProcessedPostDetailsModel>.Failure>();
    }

    [Fact]
    public async Task GetSourceProcessedPost_ShouldNameTheInferenceEachPassRanAs_AndPriceItFromTheLog()
    {
        await using var host = await TagsHost.StartAsync(configure: services => services.AddHarvestSources());
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi dekabrda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(
            domains: [new { node = "style-fashion/womens-fashion", primary = true }])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        var untagged = await host.SaveAsync("later", "A later post", "Queued, not tagged yet.");

        var overview = host.Scoped.GetRequiredService<ISourceOverviewBroker>();
        var details = PostHost.Value(await overview.GetSourceProcessedPostAsync(host.SourceId, saved.Post.Id));

        details.Post.PostId.Should().Be(saved.Post.Id);
        details.Evidence.Should().BeEmpty("no extraction ran");
        var pass = details.Passes.Should().ContainSingle().Subject;
        pass.Should().Match<ProcessedPassModel>(value => value.Pass == "post-tags" && value.Status == "done" && value.InferenceId != null
            && value.Answer.LatencyMs == 50 && value.Answer.InputTokens == 100 && value.Answer.OutputTokens == 20);
        (await overview.GetSourceProcessedPostAsync(host.SourceId, untagged.Post.Id)).Should().BeOfType<Result<SourceProcessedPostDetailsModel>.Failure>();
    }
}
