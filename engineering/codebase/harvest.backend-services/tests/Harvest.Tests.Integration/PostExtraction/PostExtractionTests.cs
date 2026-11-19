using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace Harvest.Tests.Integration.PostExtraction;

/// <summary>Layer 2 extraction over PostgreSQL with the real AI runner and a scripted caller: queued after tagging only for
/// wanted pairs, stored per content and use case with checked evidence, and never written to the post.</summary>
public sealed class PostExtractionTests
{
    private const string Title = "Tashkent Fashion Week";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Uzexpocentre'da bo'ladi. Kirish 50 000 so'm. "
        + "Dizaynerlar uchun ro'yxatdan o'tish 1 dekabrgacha davom etadi.";
    private const string User = "{{title}}\n{{text}}\n{{dates}}\n{{prices}}\n{{entityType}}\n{{domains}}\n{{timeframe}}\n{{titleEn}}";
    private const string Work = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind";

    [Fact]
    public async Task Tagging_ShouldQueueExtractionForAWantedPair_AndStoreTheCheckedAnswerWithoutTouchingThePost()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/womens-fashion", primary = true }])));
        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(0, "extraction waits for done tags");

        (await host.Tags.ProcessDueAsync()).Should().Be(1);

        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(1);
        (await WorkAsync(host, saved.Post.Id, "extract-listing")).Should().Be(0);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(EventAnswer()));

        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        (await host.ScalarAsync<string>("""
            SELECT status || '|' || role || '|' || model || '|' || prompt_version || '|' || (inference_id IS NOT NULL) || '|' || revision
                || '|' || attempts || '|' || jsonb_array_length(flags)
            FROM harvest_post_extractions WHERE post_id=@post AND use_case='extract-event'
            """, ("post", saved.Post.Id))).Should().Be("done|announcement|anthropic/claude-sonnet-5-5|1|true|2|1|0");
        using (var answer = JsonDocument.Parse(await AnswerAsync(host, saved.Post.Id)))
        {
            var root = answer.RootElement;
            root.GetProperty("name").GetProperty("en").GetString().Should().Be("Tashkent Fashion Week");
            root.GetProperty("occurrences")[0].GetProperty("starts").GetString().Should().Be("2026-12-12");
            root.GetProperty("prices")[0].GetProperty("amount").GetDecimal().Should().Be(50_000m);
            root.GetProperty("registration").GetProperty("deadline").GetProperty("value").GetString().Should().Be("2026-12-01");
            root.GetProperty("fashion").GetProperty("segments")[0].GetString().Should().Be("apparel");
        }
        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(0);
        (await Extraction(host).ProcessDueAsync()).Should().Be(0);

        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, saved.Post.Id));
        post.Revision.Should().Be(2, "the tags raised one revision; the extraction writes no post");
        post.Base.Extras.Keys.Should().NotContain(key => key.StartsWith("extract", StringComparison.OrdinalIgnoreCase));
        var call = host.Caller.Requests.Should().HaveCount(2).And.Subject.Last();
        call.User.Should().Be($"{Title}\n{Text}\nnone\nnone\nevent\nstyle-fashion/womens-fashion\nupcoming\nTashkent Fashion Week");
    }

    [Fact]
    public async Task Tagging_ShouldQueueNothing_ForPairsNobodyWants()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var listing = await host.SaveAsync("flat", "2-room flat", "Ijaraga 2 xonali kvartira.");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var business = await host.SaveAsync("forum", "Business forum", "Biznes forumi dekabrda.");
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var secondary = await host.SaveAsync("expo", "Textile expo", "To'qimachilik ko'rgazmasi dekabrda.");
        host.Caller.Enqueue(
            ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "listing", domains: [new { node = "real-estate", primary = true }])),
            ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "business-and-finance", primary = true }])),
            ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "business-and-finance", primary = true },
                new { node = "style-fashion", primary = false }])));

        (await host.Tags.ProcessDueAsync()).Should().Be(3);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_work WHERE kind<>'post-tags'")).Should().Be(1);
        (await WorkAsync(host, secondary.Post.Id, "extract-event")).Should().Be(1, "a secondary style-fashion node matches");
        (await WorkAsync(host, listing.Post.Id, "extract-listing")).Should().Be(0);
        (await WorkAsync(host, business.Post.Id, "extract-event")).Should().Be(0);

        await using var none = await StartAsync();
        var unwanted = await none.SaveAsync("fw", Title, Text);
        none.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await none.Tags.ProcessDueAsync()).Should().Be(1);
        (await WorkAsync(none, unwanted.Post.Id, "extract-event")).Should().Be(0, "no pair is wanted");
    }

    [Fact]
    public async Task ContentChange_ShouldQueueExtractionAgain_OnceItsNewTagsAreDone()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(EventAnswer()));
        await host.Tags.ProcessDueAsync();
        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        host.Clock.Advance(TimeSpan.FromHours(1));
        const string moved = "Moda haftaligi 12-14 dekabr kunlari bo'lmaydi: u 20-22 yanvarga ko'chdi. Kirish 50 000 so'm.";
        await host.SaveAsync("fw", Title, moved);

        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(0, "the new text waits for its tags");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(1);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(EventAnswer(
            occurrences: [Occurrence("2027-01-20", "2027-01-22", "20-22 yanvarga")], role: "update", changes: ["dates", "status"],
            relationStatus: "postponed", status: "postponed")));
        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        (await host.ScalarAsync<string>("""
            SELECT string_agg(role || ':' || (answer->'relation'->'changes')::text, ',' ORDER BY created_at)
            FROM harvest_post_extractions WHERE post_id=@post
            """, ("post", saved.Post.Id))).Should().Be("""announcement:[],update:["dates", "status"]""");
    }

    [Fact]
    public async Task RejectedQuote_ShouldBecomeUnknownWithAFlag_AndTheReportShouldCountIt()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(EventAnswer(
            occurrences: [Occurrence("2026-12-12", "2026-12-14", "12-14 dekabr"), Occurrence("2027-01-15", null, "15 yanvar")],
            prices: [Price(50_000m, "Kirish 50 000 so'm"), Price(80_000m, "Kirish 50 000 so'm")])));
        await host.Tags.ProcessDueAsync();

        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        using (var answer = JsonDocument.Parse(await AnswerAsync(host, saved.Post.Id)))
        {
            answer.RootElement.GetProperty("occurrences").EnumerateArray().Select(value => value.GetProperty("evidence").GetString())
                .Should().Equal("12-14 dekabr");
            answer.RootElement.GetProperty("prices").GetArrayLength().Should().Be(1);
        }
        (await host.ScalarAsync<string>("""
            SELECT string_agg(f->>'field' || ':' || (f->>'reason') || ':' || COALESCE(f->>'proposed',''), ',' ORDER BY f->>'field')
            FROM harvest_post_extractions e, jsonb_array_elements(e.flags) f WHERE e.post_id=@post
            """, ("post", saved.Post.Id))).Should().Be($"occurrences:{PostExtractionValidation.QuoteNotFound}:,"
            + $"prices:{PostExtractionValidation.AmountNotQuoted}:80000");

        var report = (await Extraction(host).ReportAsync()).ValueOrThrow().UseCases["extract-event"];
        report.Statuses.Should().Equal(new Dictionary<string, long> { ["done"] = 1 });
        report.Roles.Should().Equal(new Dictionary<string, long> { ["announcement"] = 1 });
        report.Flags.Should().ContainEquivalentOf(new { Field = "prices", Proposed = "80000", Count = 1L });
        report.Attempts.Should().Be(1);
        report.P95LatencyMs.Should().Be(50);
    }

    [Fact]
    public async Task StoredRows_ShouldHoldNoPostTextBeyondEvidenceQuotes()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(EventAnswer(
            organizer: "Call +998 90 123 45 67")));
        await host.Tags.ProcessDueAsync();
        await Extraction(host).ProcessDueAsync();

        string row = await host.ScalarAsync<string>("SELECT row_to_json(e)::text FROM harvest_post_extractions e WHERE post_id=@post",
            ("post", saved.Post.Id));
        string quotes = await host.ScalarAsync<string>("""
            SELECT string_agg(v #>> '{}', '|') FROM harvest_post_extractions e, jsonb_path_query(e.answer, 'strict $.**.evidence') v
            WHERE e.post_id=@post
            """, ("post", saved.Post.Id));

        string stored = row;
        foreach (var quote in quotes.Split('|'))
            stored = stored.Replace(quote, "", StringComparison.Ordinal);
        stored.Should().NotContain(Text);
        foreach (var sentence in Text.Split(". ", StringSplitOptions.RemoveEmptyEntries))
            stored.Should().NotContain(sentence.TrimEnd('.'));
        stored.Should().NotContain("+998").And.NotContain("123 45 67");
        row.Should().Contain("contacts", "the organizer's phone was removed with a flag");
        quotes.Split('|').Should().BeEquivalentTo("12-14 dekabr", "Kirish 50 000 so'm", "1 dekabrgacha");
    }

    [Fact]
    public async Task Backfill_ShouldQueueTaggedPosts_ForANewlyWantedPair()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var flat = await host.SaveAsync("flat", "2-room flat", "Ijaraga 2 xonali kvartira. Narxi 500 USD oyiga. Depozit: 500 USD.");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "listing",
            domains: [new { node = "real-estate", primary = true }])));
        await host.Tags.ProcessDueAsync();
        (await Extraction(host).BackfillAsync(new PostExtractionBackfillRequest())).ValueOrThrow().Should().Be(0);

        host.Scoped.GetRequiredService<IOptionsMonitor<PostExtractionOptions>>().CurrentValue.Wanted
            .Add(new PostExtractionWant { EntityType = "listing", Domain = "real-estate" });

        (await Extraction(host).BackfillAsync(new PostExtractionBackfillRequest
        {
            SinceUtc = host.Clock.GetUtcNow().AddMinutes(1),
        })).ValueOrThrow().Should().Be(0);
        (await Extraction(host).BackfillAsync(new PostExtractionBackfillRequest())).ValueOrThrow().Should().Be(1);
        (await WorkAsync(host, flat.Post.Id, "extract-listing")).Should().Be(1);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(ListingAnswer()));

        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        using (var answer = JsonDocument.Parse(await AnswerAsync(host, flat.Post.Id, "extract-listing")))
        {
            answer.RootElement.GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(500m);
            answer.RootElement.GetProperty("rentalTerms").GetProperty("deposit").GetProperty("status").GetString().Should().Be("present");
            answer.RootElement.GetProperty("author").EnumerateObject().Select(property => property.Name).Should().Equal("kind");
        }
        (await Extraction(host).BackfillAsync(new PostExtractionBackfillRequest())).ValueOrThrow().Should().Be(0, "the content has its answer");
    }

    [Fact]
    public async Task Run_ShouldRefuseAnUntaggedPost_AndStoreNothingOnADryRun()
    {
        await using var host = await StartAsync();
        var saved = await host.SaveAsync("fw", Title, Text);
        (await Extraction(host).RunAsync(saved.Post.Id)).Should().BeOfType<Result<PostExtractionRunModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Answer(EventAnswer()));
        await host.Tags.ProcessDueAsync();

        var dry = (await Extraction(host).RunAsync(saved.Post.Id, dryRun: true)).ValueOrThrow();

        dry.Status.Should().Be("done");
        dry.UseCase.Should().Be("extract-event");
        dry.Stored.Should().BeFalse();
        dry.Answer!.Value.GetProperty("format").GetString().Should().Be("runway");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_post_extractions")).Should().Be(0);
        var run = (await Extraction(host).RunAsync(saved.Post.Id)).ValueOrThrow();
        run.Stored.Should().BeTrue();
        run.FromCache.Should().BeTrue("the dry run's answer is stored in the inference log for the same prompt and input");
    }

    [Fact]
    public async Task Process_ShouldStoreAFailureWithoutAnAnswer_AndRetryItLater()
    {
        await using var host = await StartAsync(Want("event", "style-fashion"));
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer()), ScriptedAiModelCaller.Error(AiErrorKind.RateLimited));
        await host.Tags.ProcessDueAsync();

        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        (await host.ScalarAsync<string>("SELECT status || '|' || error || '|' || (answer IS NULL) FROM harvest_post_extractions WHERE post_id=@post",
            ("post", saved.Post.Id))).Should().Be("failed|rate-limited|true");
        (await Extraction(host).ProcessDueAsync()).Should().Be(0, "the retry waits for its delay");
        host.Clock.Advance(TimeSpan.FromMinutes(16));
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(EventAnswer()));
        (await Extraction(host).ProcessDueAsync()).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT status || '|' || attempts FROM harvest_post_extractions WHERE post_id=@post",
            ("post", saved.Post.Id))).Should().Be("done|2");
    }

    private static async Task<TagsHost> StartAsync(params PostExtractionWant[] wanted)
    {
        var host = await TagsHost.StartAsync(configure: services => services.AddHarvestPostExtraction()
            .Configure(options => options.Wanted = [.. wanted]));
        var prompts = host.Scoped.GetRequiredService<IPromptsBroker>();
        foreach (var useCase in PostExtractionConstants.UseCases)
        {
            (await prompts.PutUseCaseAsync(new UseCaseModel
            {
                Key = useCase, PromptMode = PromptMode.Versioned, OutputSchema = PostExtractionDefinitions.OutputSchema(useCase), KeepOutputs = true,
            })).ValueOrThrow();
            (await prompts.SavePromptAsync(new PromptDraft { UseCase = useCase, SystemTemplate = "Extract.", UserTemplate = User, By = "test" }))
                .ValueOrThrow();
        }
        return host;
    }

    private static PostExtractionWant Want(string entityType, string domain) => new() { EntityType = entityType, Domain = domain };

    private static IPostExtractionBroker Extraction(TagsHost host) => host.Scoped.GetRequiredService<IPostExtractionBroker>();

    private static Task<long> WorkAsync(TagsHost host, Guid postId, string kind) => host.ScalarAsync<long>(Work, ("post", postId), ("kind", kind));

    private static Task<string> AnswerAsync(TagsHost host, Guid postId, string useCase = "extract-event") => host.ScalarAsync<string>(
        "SELECT answer::text FROM harvest_post_extractions WHERE post_id=@post AND use_case=@useCase AND status='done' ORDER BY created_at DESC LIMIT 1",
        ("post", postId), ("useCase", useCase));

    private static object Occurrence(string starts, string? ends, string evidence) =>
        new { starts, ends, precision = "day", certainty = "stated", evidence };

    private static object Price(decimal amount, string evidence) => new { amount, currency = "UZS", label = "entry", evidence };

    internal static string EventAnswer(object[]? occurrences = null, object[]? prices = null, string role = "announcement",
        string[]? changes = null, string? relationStatus = null, string status = "scheduled", string? organizer = "Fashion Council") =>
        JsonSerializer.Serialize(new
        {
            name = new { en = "Tashkent Fashion Week", original = "Moda haftaligi" },
            editionLabel = (string?)null,
            occurrences = occurrences ?? [Occurrence("2026-12-12", "2026-12-14", "12-14 dekabr")],
            place = new { venue = "Uzexpocentre", city = "Tashkent", country = "Uzbekistan" },
            organizer,
            format = "runway",
            access = "ticketed",
            prices = prices ?? [Price(50_000m, "Kirish 50 000 so'm")],
            registration = new { opens = (object?)null, deadline = new { value = "2026-12-01", precision = "day", evidence = "1 dekabrgacha" }, link = (string?)null },
            participation = new[] { "visitor", "exhibitor" },
            status,
            relation = new { role, changes = changes ?? [], status = relationStatus },
            fashion = new { segments = new[] { "apparel" }, garmentCategories = new[] { "womenswear" }, exhibitorProfile = new[] { "designers" }, audience = "mixed" },
            flags = Array.Empty<object>(),
        });

    internal static string ListingAnswer() => JsonSerializer.Serialize(new
    {
        title = "2-room flat for rent",
        offerType = "rent",
        categoryPath = new[] { "Ko'chmas mulk", "Ijara" },
        price = new { amount = 500m, currency = "USD", period = "month", evidence = "Narxi 500 USD oyiga" },
        location = new { district = (string?)null, city = "Tashkent", address = (string?)null },
        item = new { rooms = 2, areaM2 = (decimal?)null, floor = (int?)null, floors = (int?)null, building = (string?)null, brand = (string?)null,
            model = (string?)null, year = (int?)null },
        author = new { kind = "owner" },
        relation = new { role = "announcement", changes = Array.Empty<string>(), status = (string?)null },
        rentalTerms = new
        {
            deposit = new { status = "present", claims = new[] { new { evidence = "Depozit: 500 USD", value = new { amount = 500m, currency = "USD", rentMultiple = (decimal?)null } } } },
            minimumStay = new { status = "unknown", claims = Array.Empty<object>() },
            advanceRent = new { status = "unknown", claims = Array.Empty<object>() },
            utilities = new { status = "unknown", claims = Array.Empty<object>() },
        },
        flags = Array.Empty<object>(),
    });
}
