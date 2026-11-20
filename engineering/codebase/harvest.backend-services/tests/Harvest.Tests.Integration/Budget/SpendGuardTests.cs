using AwesomeAssertions;
using Harvest.Tests.Integration.PostExtraction;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Prices;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Integration.Budget;

/// <summary>The AI spend guard in the passes over PostgreSQL with the real AI module, its inference log and a scripted
/// caller: every route while open, the last route alone at soft, no claim and no operator run at hard, and the window
/// that resets. A $0.10 limit with its soft threshold at $0.05 and Haiku priced at $0.10 per million input tokens make
/// 600,000 input tokens (about $0.06) soft and a second one hard. No live model call.</summary>
public sealed class SpendGuardTests
{
    private const string Sonnet = "anthropic/claude-sonnet-5-5";
    private const string Haiku = "anthropic/claude-haiku-5-5";
    private const string Title = "Tashkent Fashion Week";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Uzexpocentre'da bo'ladi. Kirish 50 000 so'm. "
        + "Dizaynerlar uchun ro'yxatdan o'tish 1 dekabrgacha davom etadi.";
    private const int SoftTokens = 600_000;
    private const string TagsRow = """
        SELECT attempts::text || '|' || (lease_owner IS NULL)::text || '|' || coalesce(not_before::text, '-') || '|' || coalesce(last_error, '-')
        FROM harvest_post_work WHERE post_id=@post AND kind=@kind
        """;

    [Fact]
    public async Task Tags_ShouldTryEveryRouteWhileOpen_ThenTheLastAtSoft_ClaimNothingAtHard_AndResumeInTheNextWindow()
    {
        await using var host = await StartAsync();
        await host.SaveAsync("open", Title, "Moda haftaligi dekabrda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Error(AiErrorKind.RateLimited),
            ScriptedAiModelCaller.Answer(TagsHost.Answer(), inputTokens: SoftTokens));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        Routes(host, 0).Should().Equal(Sonnet, Haiku);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var soft = await Budget(host).ReadAsync();
        soft.State.Should().Be(BudgetState.Soft);
        soft.SpentMicroUsd.Should().BeInRange(50_000, 99_999);
        soft.Rows.Select(row => (row.UseCase, row.Model, row.Attempts)).Should().BeEquivalentTo(
            new[] { ("post-tags", Sonnet, 1L), ("post-tags", Haiku, 1L) }, "the failed attempt counts as an attempt and costs nothing");

        await host.SaveAsync("soft", Title, "Moda haftaligi yanvarda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(), inputTokens: SoftTokens));
        int seen = host.Caller.Requests.Count;

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        Routes(host, seen).Should().Equal(new[] { Haiku }, "a soft guard keeps only the last route");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        (await Budget(host).ReadAsync()).State.Should().Be(BudgetState.Hard);

        var held = await host.SaveAsync("held", Title, "Moda haftaligi fevralda bo'ladi.");
        string before = await host.ScalarAsync<string>(TagsRow, ("post", held.Post.Id), ("kind", PostTagsConstants.WorkKind));
        seen = host.Caller.Requests.Count;

        (await host.Tags.ProcessDueAsync()).Should().Be(0, "a hard guard claims nothing");

        host.Caller.Requests.Should().HaveCount(seen);
        (await host.ScalarAsync<string>(TagsRow, ("post", held.Post.Id), ("kind", PostTagsConstants.WorkKind))).Should().Be(before,
            "the row stays queued with its lease and attempts untouched");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_tags WHERE post_id=@post", ("post", held.Post.Id))).Should().Be(0);

        host.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 5, 0, TimeSpan.Zero));
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));

        (await host.Tags.ProcessDueAsync()).Should().Be(1, "the next window has spent nothing");

        var december = await Budget(host).ReadAsync();
        december.State.Should().Be(BudgetState.Open);
        december.WindowStartUtc.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        december.SpentMicroUsd.Should().BeLessThan(1_000);
        (await host.ScalarAsync<string>("SELECT status FROM harvest_post_tags WHERE post_id=@post", ("post", held.Post.Id))).Should().Be("done");
    }

    [Fact]
    public async Task Tags_ShouldStoreUnavailableWithoutACall_WhenTheGuardTurnsHardInTheMiddleOfABatch()
    {
        await using var host = await StartAsync();
        await host.SaveAsync("first", Title, "Moda haftaligi dekabrda bo'ladi.");
        await host.SaveAsync("second", Title, "Moda haftaligi yanvarda bo'ladi.");
        // The first answer costs the whole limit, and a minute passes before the second row is called.
        host.Caller.Respond = _ =>
        {
            host.Clock.Advance(TimeSpan.FromMinutes(1));
            return ScriptedAiModelCaller.Answer(TagsHost.Answer(), inputTokens: 1_000_000);
        };

        (await host.Tags.ProcessDueAsync()).Should().Be(2);

        host.Caller.Requests.Should().ContainSingle();
        (await host.ScalarAsync<string>("SELECT string_agg(status || ':' || coalesce(error, ''), ',' ORDER BY status) FROM harvest_post_tags"))
            .Should().Be("done:,unavailable:budget-exhausted");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE kind='post-tags'")).Should().Be(1,
            "the unavailable row retries once the window allows it");
    }

    [Fact]
    public async Task TagsRun_ShouldTryEveryRouteWhileOpen_ThenTheLastAtSoft_AndBeRefusedAtHard()
    {
        await using var host = await StartAsync(enabled: false);
        var first = await host.SaveAsync("one", Title, "Moda haftaligi dekabrda bo'ladi.");
        var second = await host.SaveAsync("two", Title, "Moda haftaligi yanvarda bo'ladi.");
        var third = await host.SaveAsync("three", Title, "Moda haftaligi fevralda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Error(AiErrorKind.RateLimited),
            ScriptedAiModelCaller.Answer(TagsHost.Answer(), inputTokens: SoftTokens));

        (await host.Tags.RunAsync(first.Post.Id)).ValueOrThrow().Status.Should().Be("done");

        Routes(host, 0).Should().Equal(Sonnet, Haiku);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(), inputTokens: SoftTokens));
        int seen = host.Caller.Requests.Count;

        (await host.Tags.RunAsync(second.Post.Id)).ValueOrThrow().Status.Should().Be("done");

        Routes(host, seen).Should().Equal(new[] { Haiku }, "an operator run takes the last route at soft");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        seen = host.Caller.Requests.Count;

        foreach (bool dryRun in new[] { false, true })
        {
            var refused = (await host.Tags.RunAsync(third.Post.Id, dryRun)).Should().BeOfType<Result<PostTagsRunModel>.Failure>().Subject;
            refused.Error.Type.Should().Be(AppErrorType.Conflict);
            refused.Error.Message.Should().StartWith("The AI spend limit for this window is reached (")
                .And.Contain(" of 0.1000 USD); raise Harvest:Processing:Budget:LimitUsd or wait for the next window.");
        }
        host.Caller.Requests.Should().HaveCount(seen);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_tags WHERE post_id=@post", ("post", third.Post.Id))).Should().Be(0);
    }

    [Fact]
    public async Task Extraction_ShouldTryEveryRouteWhileOpen_ThenTheLastAtSoft_ClaimNothingAtHard_AndRefuseARun()
    {
        await using var host = await PostExtractionTests.StartAsync(enabled: true, services =>
        {
            services.Configure<PostExtractionOptions>(options => { options.Models = [Sonnet, Haiku]; options.BatchSize = 1; });
            Guard(services);
        }, PostExtractionTests.Want("event", "style-fashion"));
        await PriceHaikuAsync(host);
        foreach (string key in new[] { "a", "b", "c" })
        {
            await host.SaveAsync(key, Title, $"{Text} Nomer {key}.");
            host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        }
        (await host.Tags.ProcessDueAsync()).Should().Be(3);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE kind='extract-event'")).Should().Be(3);
        var extraction = PostExtractionTests.Extraction(host);
        int seen = host.Caller.Requests.Count;
        host.Caller.Enqueue(ScriptedAiModelCaller.Error(AiErrorKind.RateLimited),
            ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer(), inputTokens: SoftTokens));

        (await extraction.ProcessDueAsync()).Should().Be(1);

        Routes(host, seen).Should().Equal(Sonnet, Haiku);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        (await Budget(host).ReadAsync()).State.Should().Be(BudgetState.Soft);
        seen = host.Caller.Requests.Count;
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(PostExtractionTests.EventAnswer(), inputTokens: SoftTokens));

        (await extraction.ProcessDueAsync()).Should().Be(1);

        Routes(host, seen).Should().Equal(new[] { Haiku }, "a soft guard keeps only the last route");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        (await Budget(host).ReadAsync()).State.Should().Be(BudgetState.Hard);
        var queued = await host.ScalarAsync<Guid>("SELECT post_id FROM harvest_post_work WHERE kind='extract-event'");
        string before = await host.ScalarAsync<string>(TagsRow, ("post", queued), ("kind", PostExtractionConstants.EventUseCase));
        seen = host.Caller.Requests.Count;

        (await extraction.ProcessDueAsync()).Should().Be(0, "a hard guard claims nothing");

        (await extraction.RunAsync(queued)).Should().BeOfType<Result<PostExtractionRunModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);
        host.Caller.Requests.Should().HaveCount(seen);
        (await host.ScalarAsync<string>(TagsRow, ("post", queued), ("kind", PostExtractionConstants.EventUseCase))).Should().Be(before);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_extractions WHERE status='done'")).Should().Be(2);
    }

    [Fact]
    public async Task Rerun_ShouldQueueAtAnyState_AndAddTheReadingToItsDryRun()
    {
        await using var host = await StartAsync(more: services => services.AddHarvestPostReprocessing());
        var post = await host.SaveAsync("one", Title, "Moda haftaligi dekabrda bo'ladi.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(), inputTokens: 1_000_000));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var reruns = host.Scoped.GetRequiredService<IPostReprocessingBroker>();

        var dry = (await reruns.RerunAsync(new PostRerunRequest { AllSources = true, UseCases = [PostTagsConstants.UseCase], DryRun = true }))
            .ValueOrThrow();

        dry.DryRun.Should().BeTrue();
        dry.Budget.Should().NotBeNull();
        dry.Budget!.State.Should().Be(BudgetState.Hard);
        dry.Budget.LimitMicroUsd.Should().Be(100_000);
        dry.Budget.SpentMicroUsd.Should().BeGreaterThanOrEqualTo(100_000);

        var queued = (await reruns.RerunAsync(new PostRerunRequest { PostIds = [post.Post.Id], UseCases = [PostTagsConstants.UseCase] })).ValueOrThrow();

        queued.RunId.Should().NotBeNull("a re-run queues even at hard");
        queued.Queued.Should().Be(1);
        queued.Budget.Should().BeNull();
        (await host.Tags.ProcessDueAsync()).Should().Be(0, "the worker holds the spend back");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind='post-tags'", ("post", post.Post.Id))).Should().Be(1);
    }

    private static async Task<TagsHost> StartAsync(bool enabled = true, Action<IServiceCollection>? more = null)
    {
        var host = await TagsHost.StartAsync(enabled, configure: services =>
        {
            services.Configure<PostTagsOptions>(options => options.Models = [Sonnet, Haiku]);
            Guard(services);
            more?.Invoke(services);
        });
        await PriceHaikuAsync(host);
        return host;
    }

    private static void Guard(IServiceCollection services) => services.Configure<ProcessingBudgetOptions>(options =>
    {
        options.LimitUsd = 0.10m;
        options.SoftShare = 0.5m;
    });

    private static async Task PriceHaikuAsync(TagsHost host) =>
        (await host.Scoped.GetRequiredService<IModelPricesBroker>().PutAsync(new ModelPriceModel
        {
            Provider = "anthropic", Model = "claude-haiku-5-5", EffectiveFromUtc = host.Clock.GetUtcNow().AddDays(-1),
            InputPerMTokMicroUsd = 100_000, OutputPerMTokMicroUsd = 500_000,
        })).ValueOrThrow();

    private static IProcessingBudget Budget(TagsHost host) => host.Scoped.GetRequiredService<IProcessingBudget>();

    private static string[] Routes(TagsHost host, int skip) => [.. host.Caller.Requests.Skip(skip).Select(request => request.Model.ToString())];
}
