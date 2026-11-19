using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
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
            && answer.PromptVersion == 1 && answer.LatencyMs == 50 && answer.CostMicroUsd != null);
    }
}
