using AwesomeAssertions;
using Harvest.Tests.Integration.PostExtraction;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>AI processing for a source whose entry the instance turned off: the model-processing fragments read it as <c>off</c>, so
/// the tags and extraction queues take nothing and send nothing to a model, and turning the entry on makes its posts eligible again.</summary>
public sealed class SourceCatalogAiTests
{
    private const string Work = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind";

    [Fact]
    public async Task Tags_ShouldNeitherQueueNorSendToAModel_ForAnOffSource_AndResumeWhenItsEntryIsOn()
    {
        await using var host = await TagsHost.StartAsync();
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Month", "Moda haftaligi dekabrda bo'ladi.");
        (await host.ScalarAsync<long>(Work, ("post", saved.Post.Id), ("kind", "post-tags"))).Should().Be(1, "the post queued while its source was on");
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);

        (await host.Tags.ProcessDueAsync()).Should().Be(1, "the claim takes the row and re-checks the source when it acts, not when the row was queued");
        host.Caller.Requests.Should().BeEmpty("the source reads as off, so no model call reaches its post");
        (await host.ScalarAsync<long>(Work, ("post", saved.Post.Id), ("kind", "post-tags"))).Should().Be(0, "an off source's row is dropped, as a source in mode off's is");
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest())).ValueOrThrow().Should().Be(0, "the backfill queues nothing for an off source");
        (await host.Tags.RunAsync(saved.Post.Id)).Should().BeOfType<Result<PostTagsRunModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Forbidden, "an operator's explicit run reads the source as off too");

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        (await host.Tags.BackfillAsync(new PostTagsBackfillRequest())).ValueOrThrow().Should().Be(1, "turning the entry on makes the post eligible again");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        host.Caller.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Extraction_ShouldSendNothingToAModel_ForAnOffSource()
    {
        await using var host = await PostExtractionTests.StartAsync(PostExtractionTests.Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi dekabrda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/womens-fashion", primary = true }])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<long>(Work, ("post", saved.Post.Id), ("kind", "extract-event"))).Should().Be(1, "done tags queued the wanted extraction");
        var requests = host.Caller.Requests.Count;
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);

        (await PostExtractionTests.Extraction(host).ProcessDueAsync()).Should().Be(1, "the claim re-checks the source when it acts");

        host.Caller.Requests.Should().HaveCount(requests, "no model call reaches an off source's post");
        (await host.ScalarAsync<long>(Work, ("post", saved.Post.Id), ("kind", "extract-event"))).Should().Be(0, "an off source's row is dropped, as a source in mode off's is");
    }
}
