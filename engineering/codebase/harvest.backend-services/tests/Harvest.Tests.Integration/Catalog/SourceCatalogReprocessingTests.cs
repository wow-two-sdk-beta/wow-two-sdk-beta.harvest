using AwesomeAssertions;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>A re-run for a source whose entry the instance turned off: it queues nothing at any scope, the claim drops a row queued
/// before the switch without a model call, and an eval set samples none of its posts. Turning the entry on queues them again.</summary>
public sealed class SourceCatalogReprocessingTests
{
    private const string Title = "Tashkent Fashion Week";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Uzexpocentre'da bo'ladi. Kirish 50 000 so'm.";
    private const string Work = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind";

    [Fact]
    public async Task Rerun_ShouldQueueNothingAndRefuseAnOffSource_AtEveryScope_AndQueueAgainWhenItsEntryIsOn()
    {
        await using var host = await StartAsync();
        var saved = await TaggedPostAsync(host);
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);

        var byPost = (await Reruns(host).RerunAsync(new PostRerunRequest { PostIds = [saved], UseCases = [PostTagsConstants.UseCase] })).ValueOrThrow();
        var bySource = (await Reruns(host).RerunAsync(new PostRerunRequest { SourceIds = [host.SourceId], UseCases = [PostTagsConstants.UseCase] })).ValueOrThrow();
        var everything = (await Reruns(host).RerunAsync(new PostRerunRequest { AllSources = true, UseCases = [PostTagsConstants.UseCase] })).ValueOrThrow();

        foreach (var run in new[] { byPost, bySource, everything })
            (run.Queued, run.Refused).Should().Be((0, 1), $"the {run.Scope} scope reads the source as off");
        (await host.ScalarAsync<long>(Work, ("post", saved), ("kind", PostTagsConstants.UseCase))).Should().Be(0, "no row was queued");

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        var resumed = (await Reruns(host).RerunAsync(new PostRerunRequest { PostIds = [saved], UseCases = [PostTagsConstants.UseCase] })).ValueOrThrow();
        (resumed.Queued, resumed.Refused).Should().Be((1, 0), "turning the entry on makes the post eligible again");
    }

    [Fact]
    public async Task Claim_ShouldDropARerunRowQueuedBeforeTheSwitch_AndCallNoModel()
    {
        await using var host = await StartAsync();
        var saved = (await host.SaveAsync("fw", Title, Text)).Post.Id;
        var queued = (await Reruns(host).RerunAsync(new PostRerunRequest { PostIds = [saved], UseCases = [PostTagsConstants.UseCase] })).ValueOrThrow();
        queued.Queued.Should().Be(1, "the source was on when the re-run was asked for");
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));

        (await host.Tags.ProcessDueAsync()).Should().Be(1, "the claim takes the row and reads the source when it acts, not when the row was queued");

        host.Caller.Requests.Should().BeEmpty("no model call reaches the post of an off source, though one was scripted to answer");
        (await host.ScalarAsync<long>(Work, ("post", saved), ("kind", PostTagsConstants.UseCase))).Should().Be(0, "the row is dropped, not left queued");
    }

    [Fact]
    public async Task EvalSet_ShouldSampleNoPostOfAnOffSource_AndSampleItAgainWhenItsEntryIsOn()
    {
        await using var host = await StartAsync();
        await TaggedPostAsync(host);
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);
        var request = new PostEvalSetRequest
        {
            Set = "fashion-tags", UseCase = PostTagsConstants.UseCase, Sample = 2, Seed = 7, Expected = "current", ScoreFields = ["entityType"],
        };

        var off = (await Reruns(host).BuildEvalSetAsync(request)).ValueOrThrow();

        off.Sampled.Should().Be(0, "the sample leaves out a post a turned-off source supplies");
        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        (await Reruns(host).BuildEvalSetAsync(request)).ValueOrThrow().Sampled.Should().Be(1);
    }

    /// <summary>Saves a post and lets the tags pass answer it, so its work row is gone and a current answer is stored.</summary>
    private static async Task<Guid> TaggedPostAsync(TagsHost host)
    {
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        return saved.Post.Id;
    }

    private static Task<TagsHost> StartAsync() => TagsHost.StartAsync(configure: services =>
    {
        services.AddHarvestPostExtraction();
        services.AddHarvestEntities().Configure(options => options.Enabled = false);
        services.AddHarvestPostReprocessing();
    });

    private static IPostReprocessingBroker Reruns(TagsHost host) => host.Scoped.GetRequiredService<IPostReprocessingBroker>();
}
