using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Tagging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestSources.Tests.E2E;

/// <summary>The <c>post-tags</c> host verb on the source host: prompt registration, a run, a backfill and a report,
/// answered by the scripted caller — no live model call, and no post text in the report.</summary>
public sealed class PostTagsVerbTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1";
    private const string AiEnabledSetting = "Harvest:Processing:Ai:Enabled";
    private const string PrivateTemplate = "Synthetic private post-tags prompt";
    private const string PostText = "Moda haftaligi dekabrda bo'ladi.";

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public async Task Host_refuses_post_tags_without_shared_ai(string? enabled)
    {
        await using var host = Build(new Dictionary<string, string?> { [AiEnabledSetting] = enabled });

        var run = await RunAsync(host.Services, ["report"]);

        run.Exit.Should().Be(1);
        run.Output.Should().BeEmpty();
        run.Error.Should().Contain(AiEnabledSetting);
        host.Services.GetService<IOptions<PostTagsOptions>>()!.Value.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Host_keeps_only_tag_routes_whose_provider_is_configured()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["Ai:Providers:groq:Kind"] = "openai",
            ["Ai:Providers:groq:ApiKey"] = "fixture-provider-key",
            [$"{PostTagsConstants.SectionName}:Models:0"] = "anthropic/claude-haiku-5-5",
            [$"{PostTagsConstants.SectionName}:Models:1"] = "Groq/openai/gpt-oss-20b",
            [$"{PostTagsConstants.SectionName}:MaxTextChars"] = "3000",
        });

        var options = host.Services.GetRequiredService<IOptions<PostTagsOptions>>().Value;

        options.Models.Should().Equal("groq/openai/gpt-oss-20b");
        options.MaxTextChars.Should().Be(3000);
    }

    [Fact]
    public async Task Prompt_saves_versions_under_the_code_schema_and_refuses_unbound_drafts()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
        });
        await host.ConfigureAsync();

        var first = await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "operator"], Draft());
        var second = await RunAsync(host.Services, ["prompt", "--file", "-"], Draft());

        Version(first).Should().Be(1);
        Version(second).Should().Be(2);
        first.Output.Should().NotContain(PrivateTemplate);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var prompts = scope.ServiceProvider.GetRequiredService<IPromptsBroker>();
            var useCase = (await prompts.GetUseCaseAsync(PostTagsConstants.UseCase)).ValueOrThrow();
            useCase.PromptMode.Should().Be(PromptMode.Versioned);
            useCase.KeepOutputs.Should().BeTrue();
            using var actual = JsonDocument.Parse(useCase.OutputSchema!);
            using var expected = JsonDocument.Parse(PostTagsDefinition.OutputSchema);
            JsonElement.DeepEquals(actual.RootElement, expected.RootElement).Should().BeTrue();
            (await prompts.ListPromptsAsync(PostTagsConstants.UseCase)).ValueOrThrow().Select(prompt => prompt.CreatedBy)
                .Should().Equal("draft-author", "operator");
        }

        var unknown = await RunAsync(host.Services, ["prompt", "--file", "-"], Draft(user: "{{description}} {{text}}"));
        unknown.Exit.Should().Be(1);
        unknown.Error.Should().Contain("postedAt").And.NotContain(PrivateTemplate);
        var untaxed = await RunAsync(host.Services, ["prompt", "--file", "-"], Draft(system: PrivateTemplate));
        untaxed.Exit.Should().Be(1);
        untaxed.Error.Should().Contain("{{taxonomy}}");
        (await RunAsync(host.Services, ["prompt", "--by", "x"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["unknown"])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Run_backfill_and_report_drive_the_pass_with_a_scripted_model()
    {
        var caller = new ScriptedAiModelCaller();
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = database,
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
        }, services => services.AddSingleton<IAiModelCaller>(caller));
        await host.ConfigureAsync();
        Version(await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "operator"], Draft())).Should().Be(1);
        var postId = await SeedPostAsync(host.Services);
        caller.Enqueue(ScriptedAiModelCaller.Answer(JsonSerializer.Serialize(new
        {
            entityType = "event", domains = new[] { new { node = "style-fashion", primary = true } }, topics = new[] { "fashion-week" },
            about = "a fashion week", timeframe = "upcoming", items = 1, language = "uz", confidence = 0.9,
            titleEn = "Tashkent Fashion Week", flags = new[] { new { field = "domains", reason = "no node", proposed = "fashion-weeks" } },
        })));

        var dry = await RunAsync(host.Services, ["run", "--post", postId.ToString(), "--dry-run"]);
        dry.Exit.Should().Be(0, dry.Error);
        using (var answer = JsonDocument.Parse(dry.Output))
        {
            answer.RootElement.GetProperty("status").GetString().Should().Be("done");
            answer.RootElement.GetProperty("stored").GetBoolean().Should().BeFalse();
            answer.RootElement.GetProperty("answer").GetProperty("domains")[0].GetProperty("node").GetString().Should().Be("style-fashion");
        }
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_tags")).Should().Be(0);

        var run = await RunAsync(host.Services, ["run", "--post", postId.ToString()]);
        run.Exit.Should().Be(0, run.Error);
        using (var stored = JsonDocument.Parse(run.Output))
            stored.RootElement.GetProperty("newRevision").GetInt32().Should().Be(2);
        caller.Requests.Should().ContainSingle("the run reused the dry run's stored answer");

        var backfill = await RunAsync(host.Services, ["backfill", "--since", "2020-01-01", "--limit", "10"]);
        backfill.Output.Trim().Should().Be("""{"queued":0}""");

        var report = await RunAsync(host.Services, ["report", "--since", "2020-01-01"]);
        report.Exit.Should().Be(0, report.Error);
        report.Output.Trim().Should().NotContain("\n").And.NotContain("Moda").And.NotContain("Fashion Week");
        using (var counts = JsonDocument.Parse(report.Output))
        {
            counts.RootElement.GetProperty("statuses").GetProperty("done").GetInt64().Should().Be(1);
            counts.RootElement.GetProperty("entityTypes").GetProperty("event").GetInt64().Should().Be(1);
            counts.RootElement.GetProperty("domains").GetProperty("style-fashion").GetInt64().Should().Be(1);
            counts.RootElement.GetProperty("flags")[0].GetProperty("proposed").GetString().Should().Be("fashion-weeks");
            counts.RootElement.GetProperty("attempts").GetInt64().Should().Be(1);
        }

        (await RunAsync(host.Services, ["run", "--post", Guid.NewGuid().ToString()])).Exit.Should().Be(1);
        (await RunAsync(host.Services, ["run", "--post", "not-a-guid"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["backfill", "--limit", "0"])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Relevance_compares_the_rules_with_the_tags_and_the_relevance_filter_adds_what_the_rules_miss()
    {
        var caller = new ScriptedAiModelCaller();
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = database,
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:EntityType"] = "event",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:Domain"] = "style-fashion",
            [$"{PostExtractionConstants.SectionName}:Wanted:1:EntityType"] = "listing",
            [$"{PostExtractionConstants.SectionName}:Wanted:1:Domain"] = "real-estate",
        }, services => services.AddSingleton<IAiModelCaller>(caller));
        await host.ConfigureAsync();
        Version(await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "operator"], Draft())).Should().Be(1);

        await using var scope = host.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        var tenant = (await registry.RegisterTenantAsync(new TenantRegistration { Slug = "ocharo", Name = "Ocharo" })).ValueOrThrow();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var source = (await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "afisha.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).ValueOrThrow();
        var pipeline = (await registry.DeclarePipelineAsync(new PipelineDeclaration { ContainerId = container.Id, SourceId = source.Id }))
            .ValueOrThrow();
        async Task<Guid> SubscribeAsync(string filter) => (await delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = tenant.Id, ContainerId = container.Id, PipelineId = pipeline.Id, FilterKey = filter, Schema = PostEnvelopeConstants.EventSchema,
        })).ValueOrThrow().Id;
        var rulesFeed = await SubscribeAsync(OcharoFashionFilter.FilterKey);
        var relevanceFeed = await SubscribeAsync(OcharoRelevanceFilter.FilterKey);

        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        async Task<Guid> SaveAsync(string key, string title, string text) => (await posts.SaveCaptureAsync(container.Id, source.Id, null,
            Capture(key, title, text), new PostEnvelope
            {
                Kind = PostKind.Event, Title = title, Text = text, Language = new PostLanguage { Code = "en" },
                Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
            })).ValueOrThrow().Post.Id;
        var named = await SaveAsync("fashline", "Fashline 2026", "Fashion show in Tashkent.");
        var missed = await SaveAsync("evening", "Evening at the gallery", "Young designers present a new collection.");
        var jazz = await SaveAsync("jazz", "Jazz night", "A jazz trio plays at the club.");
        caller.Enqueue(
            ScriptedAiModelCaller.Answer(TagsAnswer("event", "style-fashion/womens-fashion")),
            ScriptedAiModelCaller.Answer(TagsAnswer("event", "style-fashion")),
            ScriptedAiModelCaller.Answer(TagsAnswer("event", "business-and-finance")));
        (await scope.ServiceProvider.GetRequiredService<IPostTagsBroker>().ProcessDueAsync()).Should().Be(3);
        var waiting = await SaveAsync("later", "Book club", "Readers meet on Friday.");

        const string Feeds = "SELECT count(*) FROM harvest_subscription_event";
        long before = await ScalarAsync<long>(database, Feeds);
        var report = await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--since", "2020-01-01"]);

        report.Exit.Should().Be(0, report.Error);
        report.Output.Trim().Should().NotContain("\n").And.NotContain("Fashline").And.NotContain("gallery").And.NotContain("jazz");
        report.Output.TrimEnd().Split('\n').Should().ContainSingle("the verb prints one JSON line");
        (await ScalarAsync<long>(database, Feeds)).Should().Be(before, "the report writes nothing");
        using (var counts = JsonDocument.Parse(report.Output))
        {
            var root = counts.RootElement;
            root.GetProperty("rules").GetString().Should().Be(OcharoFashionFilter.FilterKey);
            root.GetProperty("wanted").EnumerateArray().Select(pair => pair.GetString()).Should().Equal("event:style-fashion");
            root.GetProperty("posts").GetInt64().Should().Be(4);
            Row(root.GetProperty("rulesYes")).Should().Equal(1, 0, 0, 0);
            Row(root.GetProperty("rulesNo")).Should().Equal(1, 1, 0, 1);
            root.GetProperty("taggedPosts").GetInt64().Should().Be(3);
            root.GetProperty("invalidAnswers").GetInt64().Should().Be(0);
            root.GetProperty("attempts").GetInt64().Should().Be(3);
            root.GetProperty("latencySample").GetInt32().Should().Be(3);
            root.TryGetProperty("costPerPostMicroUsd", out _).Should().BeTrue();
        }

        var rules = await PullAsync(delivery, rulesFeed, 2);
        rules.Should().OnlyContain(item => item.PostId == named, "the rules never pass the missed collection evening");
        var relevance = await PullAsync(delivery, relevanceFeed, 3);
        relevance.Select(item => $"{(item.PostId == named ? "named" : item.PostId == missed ? "missed" : "other")}:{item.Type}:{item.Revision}")
            .Should().Equal("named:post.new:1", "named:post.changed:2", "missed:post.changed:2");
        var added = relevance.Last().Data!.Value;
        added.GetProperty("classification").GetProperty("verdict").GetString().Should().Be("relevant");
        added.GetProperty("classification").GetProperty("classifier").GetString().Should().Be("model:anthropic/claude-haiku-5-5@post-tags:1");
        added.GetProperty("kind").GetString().Should().Be("event");
        relevance.Should().NotContain(item => item.PostId == jazz || item.PostId == waiting);

        (await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--since", "not-a-date"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["relevance", "--rules", "x"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["relevance", "--since", "2020-01-01"])).Exit.Should().Be(2, "the filter and the pairs are named");
        (await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["relevance", "--filter", "no-such-filter", "--wanted", "event:style-fashion"])).Exit
            .Should().Be(1, "an unregistered filter is refused");
    }

    [Fact]
    public async Task Relevance_disagreements_lists_only_the_posts_the_rules_and_the_tags_split_on()
    {
        var caller = new ScriptedAiModelCaller();
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = Build(new Dictionary<string, string?>
        {
            [AiEnabledSetting] = "true",
            ["DB_CONNECTION"] = database,
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
        }, services => services.AddSingleton<IAiModelCaller>(caller));
        await host.ConfigureAsync();
        Version(await RunAsync(host.Services, ["prompt", "--file", "-", "--by", "operator"], Draft())).Should().Be(1);

        await using var scope = host.Services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var source = (await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "afisha.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).ValueOrThrow();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        var tags = scope.ServiceProvider.GetRequiredService<IPostTagsBroker>();
        async Task<Guid> SaveAsync(string key, string title, string text, string? entityType, string node = "style-fashion")
        {
            var id = (await posts.SaveCaptureAsync(container.Id, source.Id, null, Capture(key, title, text), new PostEnvelope
            {
                Kind = PostKind.Event, Title = title, Text = text, Language = new PostLanguage { Code = "en" },
                Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
            })).ValueOrThrow().Post.Id;
            if (entityType is not null)
            {
                caller.Enqueue(ScriptedAiModelCaller.Answer(TagsAnswer(entityType, node)));
                (await tags.RunAsync(id)).ValueOrThrow().Status.Should().Be("done");
            }
            return id;
        }

        const string Contacts = "+998 90 123 45 67 or info@fashline.uz @fashline_uz";
        string longTitle = $"Fashline 2026 call {Contacts} " + new string('x', 200);
        var rulesYesTagsNo = await SaveAsync("fashline-call", longTitle, "Apply to the fashion show.", "event", "business-and-finance");
        var rulesYesTagsUnsure = await SaveAsync("fashline-expo", "Fashline Expo", "Fashion fair in Tashkent.", "unsure");
        var agreeingYes = await SaveAsync("fashline-main", "Fashline 2026", "Fashion show in Tashkent.", "event");
        var rulesNoTagsMatch = await SaveAsync("evening", "Evening at the gallery", "Young designers present a new collection.", "event");
        var rulesNoTagsUnsure = await SaveAsync("jazz-ru", "Джаз вечер", "A jazz trio plays at the club.", "unsure");
        var agreeingNo = await SaveAsync("jazz", "Jazz night", "A jazz trio plays at the club.", "event", "business-and-finance");
        var untagged = await SaveAsync("later", "Book club", "Readers meet on Friday.", null);

        long tagRows = await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_tags");
        var review = await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--disagreements", "--since", "2020-01-01"]);

        review.Exit.Should().Be(0, review.Error);
        review.Output.TrimEnd().Split('\n').Should().ContainSingle("the verb prints one JSON line");
        review.Output.Should().NotContain("998").And.NotContain("fashline.uz").And.NotContain("fashline_uz", "contacts are scrubbed from titles");
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_post_tags")).Should().Be(tagRows, "the review writes nothing");
        using var document = JsonDocument.Parse(review.Output);
        var root = document.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().Equal("rules", "wanted", "items");
        root.GetProperty("rules").GetString().Should().Be(OcharoFashionFilter.FilterKey);
        root.GetProperty("wanted").EnumerateArray().Select(pair => pair.GetString()).Should().Equal("event:style-fashion");
        var items = root.GetProperty("items").EnumerateArray().ToArray();
        string[] yes = Ids(rulesYesTagsNo, rulesYesTagsUnsure), no = Ids(rulesNoTagsMatch, rulesNoTagsUnsure);
        items.Select(item => item.GetProperty("postId").GetString()).Should().Equal([.. yes, .. no],
            "rules yes come first, each side by post id; agreeing, untagged and the filter's own throws are not listed");
        items.Select(item => item.GetProperty("rules").GetString()).Should().Equal("yes", "yes", "no", "no");
        items.Select(item => item.GetProperty("postId").GetString()).Should().NotContain(
            [agreeingYes.ToString("D"), agreeingNo.ToString("D"), untagged.ToString("D")]);

        var first = items.Single(item => item.GetProperty("postId").GetString() == rulesYesTagsNo.ToString("D"));
        first.GetProperty("tags").GetString().Should().Be("noMatch");
        first.GetProperty("source").GetString().Should().Be("site/afisha.example");
        first.GetProperty("primaryDomain").GetString().Should().Be("business-and-finance");
        first.GetProperty("title").GetString()!.Should().StartWith("Fashline 2026 call … or … …").And.HaveLength(140);
        var unsure = items.Single(item => item.GetProperty("postId").GetString() == rulesYesTagsUnsure.ToString("D"));
        unsure.GetProperty("tags").GetString().Should().Be("unsure");
        unsure.GetProperty("entityType").GetString().Should().Be("unsure");
        var match = items.Single(item => item.GetProperty("postId").GetString() == rulesNoTagsMatch.ToString("D"));
        match.GetProperty("rules").GetString().Should().Be("no");
        match.GetProperty("tags").GetString().Should().Be("match");
        match.GetProperty("title").GetString().Should().Be("Evening at the gallery");
        match.GetProperty("titleEn").GetString().Should().Be("An event");
        match.GetProperty("entityType").GetString().Should().Be("event");
        match.GetProperty("primaryDomain").GetString().Should().Be("style-fashion");
        match.GetProperty("domains").EnumerateArray().Select(node => node.GetString()).Should().Equal("style-fashion");
        match.GetProperty("topics").EnumerateArray().Select(topic => topic.GetString()).Should().Equal("show");
        match.GetProperty("about").GetString().Should().Be("an event");
        match.GetProperty("timeframe").GetString().Should().Be("upcoming");
        match.GetProperty("confidence").GetDouble().Should().Be(0.8);
        items.Single(item => item.GetProperty("postId").GetString() == rulesNoTagsUnsure.ToString("D"))
            .GetProperty("title").GetString().Should().Be("Джаз вечер");

        var capped = await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--disagreements", "--limit", "3"]);
        capped.Exit.Should().Be(0, capped.Error);
        capped.Output.TrimEnd().Split('\n').Should().ContainSingle();
        using (var cappedItems = JsonDocument.Parse(capped.Output))
            cappedItems.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("postId").GetString())
                .Should().Equal([.. yes, no[0]], "--limit keeps the first N");
        using (var one = JsonDocument.Parse((await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--disagreements", "--limit", "1"])).Output))
            one.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);

        (await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--limit", "3"])).Exit.Should().Be(2, "a limit belongs to the review");
        (await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--disagreements", "--limit", "0"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--disagreements", "--limit", "1001"])).Exit.Should().Be(1);
        (await RunAsync(host.Services, ["relevance", "--filter", "ocharo-fashion-v1", "--wanted", "event:style-fashion", "--disagreements", "yes"])).Exit.Should().Be(2);

        static string[] Ids(params Guid[] ids) => [.. ids.Select(id => id.ToString("D")).Order(StringComparer.Ordinal)];
    }

    private static long[] Row(JsonElement row) =>
        [row.GetProperty("match").GetInt64(), row.GetProperty("noMatch").GetInt64(), row.GetProperty("unsure").GetInt64(),
            row.GetProperty("notTagged").GetInt64()];

    private static string TagsAnswer(string entityType, string node) => JsonSerializer.Serialize(new
    {
        entityType, domains = new[] { new { node, primary = true } }, topics = new[] { "show" }, about = "an event", timeframe = "upcoming",
        items = 1, language = "en", confidence = 0.8, titleEn = "An event", flags = Array.Empty<object>(),
    });

    private static Capture Capture(string key, string title, string text) => new()
    {
        ExternalKey = key, Url = $"https://afisha.example/{key}", RequestUrl = $"https://afisha.example/{key}",
        FinalUrl = $"https://afisha.example/{key}", CapturedAtUtc = DateTimeOffset.UtcNow, Body = Encoding.UTF8.GetBytes(text),
        ContentProjection = $"{title}\n{text}", HttpStatus = 200, ContentType = "text/html",
    };

    /// <summary>Pulls a subscription until it holds <paramref name="expected"/> events: a row is readable once no older
    /// transaction on the shared server is still running.</summary>
    private static async Task<IReadOnlyList<DeliveryEvent>> PullAsync(IDeliveryBroker delivery, Guid subscription, int expected)
    {
        for (var attempt = 0; ; attempt++)
        {
            var page = (await delivery.PullAsync(subscription)).ValueOrThrow();
            if (page.Data.Count >= expected || attempt == 100)
            {
                page.Data.Should().HaveCount(expected);
                return page.Data;
            }
            await Task.Delay(100);
        }
    }

    private static async Task<Guid> SeedPostAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var source = (await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "fashion.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).ValueOrThrow();
        var capture = new Capture
        {
            ExternalKey = "fw", Url = "https://fashion.example/fw", RequestUrl = "https://fashion.example/fw", FinalUrl = "https://fashion.example/fw",
            CapturedAtUtc = DateTimeOffset.UtcNow, Body = Encoding.UTF8.GetBytes(PostText), ContentProjection = PostText, HttpStatus = 200,
            ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Event, Title = "Toshkent moda haftaligi", Text = PostText, Language = new PostLanguage { Code = "uz" },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        var saved = (await scope.ServiceProvider.GetRequiredService<IPostBroker>()
            .SaveCaptureAsync(container.Id, source.Id, null, capture, envelope)).ValueOrThrow();
        return saved.Post.Id;
    }

    private static MemoryStream Draft(string? system = null, string? user = null) =>
        new(JsonSerializer.SerializeToUtf8Bytes(new PromptDraft
        {
            UseCase = PostTagsConstants.UseCase,
            SystemTemplate = system ?? PrivateTemplate + "\n{{taxonomy}}",
            UserTemplate = user ?? "{{title}}\n{{sourceCategory}}\n{{dates}}\n{{place}}\n{{postedAt}}\n{{language}}\n{{text}}",
            By = "draft-author",
        }));

    private static int Version((int Exit, string Output, string Error) run)
    {
        run.Exit.Should().Be(0, run.Error);
        using var doc = JsonDocument.Parse(run.Output);
        doc.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(new[] { "useCase", "version", "contentHash", "id" });
        return doc.RootElement.GetProperty("version").GetInt32();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static WebApplication Build(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = UnreachableDatabase,
            ["Harvest:Operator:ApiKey"] = "hv_" + new string('r', 32),
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Configure();
        services?.Invoke(builder.Services);
        return builder.Build();
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(IServiceProvider services, string[] args,
        Stream? input = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await PostTagsVerb.RunAsync(services, args, input ?? Stream.Null, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
