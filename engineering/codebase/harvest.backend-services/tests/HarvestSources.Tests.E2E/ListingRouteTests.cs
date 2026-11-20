using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Extraction;
using HarvestSources.Api.Marketplace;
using HarvestSources.Api.Ocharo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
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
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace HarvestSources.Tests.E2E;

/// <summary>The listing route on the deployed host's composition: Haven's three OLX searches tag their ads from the section with real
/// taxonomy nodes and no tagging call, queue extraction only for the modes that allow it, and deliver a done answer to the
/// subscription that holds the ad; an Afisha post keeps its tagging call. A scripted model answers, and the ads are invented.</summary>
public sealed class ListingRouteTests
{
    private const string Title = "2 xonali kvartira";
    private const string Text = "Ijaraga 2 xonali kvartira. Narxi 500 USD oyiga. Depozit: 500 USD.";
    private const string AiEnabled = "Harvest:Processing:Ai:Enabled";
    private const string Work = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind";

    [Fact]
    public async Task Host_binds_havens_wanted_pair_beside_ocharos()
    {
        await using var stack = await StartAsync();

        var options = stack.Host.Services.GetRequiredService<IOptions<PostExtractionOptions>>().Value;

        options.Wanted.Should().Equal(new PostExtractionWant { EntityType = "event", Domain = "style-fashion" },
            new PostExtractionWant { EntityType = "listing", Domain = "real-estate" });
    }

    [Fact]
    public async Task Haven_sections_tag_their_ads_in_code_with_real_nodes_and_queue_extraction_under_auto()
    {
        await using var stack = await StartAsync();
        var haven = await SetupHavenAsync(stack, "auto");

        var expected = new Dictionary<string, string>
        {
            ["apartments-rent"] = "real-estate/apartments",
            ["houses-rent"] = "real-estate/houses",
            ["daily-rent"] = "real-estate/real-estate-renting-and-leasing",
        };
        foreach (var (search, node) in expected)
        {
            var ad = await SaveAdAsync(stack, haven, search, "ID" + search[..4]);

            var post = ad.Post;
            post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:" + node, "domain:real-estate");
            post.Base.Tags.Should().OnlyContain(tag => tag.Tier == ExtractionTier.Rules && tag.Version == "section-implied@1");
            post.Base.Classification!.Verdict.Should().Be(ClassificationVerdict.Relevant);
            post.Base.Extras.Keys.Should().NotContain("postTags");
            (await ScalarAsync<long>(stack, Work, ("post", post.Id), ("kind", "post-tags"))).Should().Be(0, search);
            (await ScalarAsync<long>(stack, Work, ("post", post.Id), ("kind", "extract-listing"))).Should().Be(1, search);
            (await ScalarAsync<string>(stack, "SELECT status || '|' || (model IS NULL) || '|' || (title_en IS NULL) FROM harvest_post_tags WHERE post_id=@post",
                ("post", post.Id))).Should().Be("done|true|true");
        }
        stack.Caller.Requests.Should().BeEmpty("no tagging call went to a model");
    }

    [Fact]
    public async Task A_done_answer_reaches_only_the_subscription_that_holds_the_ad_and_a_failed_one_nothing()
    {
        await using var stack = await StartAsync();
        var haven = await SetupHavenAsync(stack, "auto");
        var apartments = await SaveAdAsync(stack, haven, "apartments-rent", "IDflat");
        var houses = await SaveAdAsync(stack, haven, "houses-rent", "IDhome");
        stack.Caller.Enqueue(ScriptedAiModelCaller.Answer(ListingAnswer()), ScriptedAiModelCaller.Error(AiErrorKind.RateLimited));

        await using (var scope = stack.Host.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IPostExtractionBroker>().ProcessDueAsync()).Should().Be(2);

        var flat = await EventsAsync(stack, haven.SubscriptionId("apartments-rent"), expected: 2);
        flat.Select(item => item.Type).Should().Equal("post.new", "post.extracted");
        flat.Should().OnlyContain(item => item.PostId == apartments.Post.Id);
        var extracted = flat[1];
        var ai = extracted.Data!.Value.GetProperty("base").GetProperty("extras").GetProperty("listingAi");
        ai.GetProperty("schema").GetString().Should().Be("extract-listing@2");
        ai.GetProperty("model").GetString().Should().Be("anthropic/claude-haiku-5-5");
        ai.GetProperty("promptVersion").GetInt32().Should().Be(1);
        ai.GetProperty("revision").GetInt32().Should().Be(extracted.Revision);
        ai.GetProperty("answer").GetProperty("price").GetProperty("amount").GetDecimal().Should().Be(500m);
        ai.GetRawText().Should().NotContain("contacts").And.NotContain("phone");
        extracted.Context.Should().ContainKey("catalog").WhoseValue.Should().Be("haven-rent-res");

        var home = await EventsAsync(stack, haven.SubscriptionId("houses-rent"), expected: 1);
        home.Select(item => item.Type).Should().Equal("post.new");
        home.Should().HaveCount(1, "the failed answer delivers nothing");
        home.Should().OnlyContain(item => item.PostId == houses.Post.Id);
        (await ScalarAsync<string>(stack, "SELECT status FROM harvest_post_extractions WHERE post_id=@post", ("post", houses.Post.Id))).Should().Be("failed");
        (await EventsAsync(stack, haven.SubscriptionId("daily-rent"))).Should().BeEmpty("a sibling search never receives the other search's ad");
        await using var read = stack.Host.Services.CreateAsyncScope();
        var stored = (await read.ServiceProvider.GetRequiredService<IPostBroker>().GetPostAsync(haven.ContainerId, apartments.Post.Id)).ValueOrThrow();
        stored.Revision.Should().Be(1, "the answer raised no revision");
        stored.Base.Extras.Keys.Should().NotContain("listingAi");
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("off")]
    public async Task Manual_and_off_tag_in_code_but_queue_nothing(string mode)
    {
        await using var stack = await StartAsync();
        var haven = await SetupHavenAsync(stack, mode);

        var ad = await SaveAdAsync(stack, haven, "apartments-rent", "IDmode");

        ad.Post.Base.Tags.Select(tag => tag.Tag).Should().Equal("type:listing", "domain:real-estate/apartments", "domain:real-estate");
        (await ScalarAsync<long>(stack, "SELECT count(*) FROM harvest_post_work WHERE post_id=@post", ("post", ad.Post.Id))).Should().Be(0);
        await using var scope = stack.Host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IPostExtractionBroker>().ProcessDueAsync()).Should().Be(0);
        stack.Caller.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tag_only_revision_raises_no_post_changed_for_a_marketplace_subscription_but_still_does_for_any_other()
    {
        await using var stack = await StartAsync();
        var haven = await SetupHavenAsync(stack, "auto");
        var marketplace = haven.SubscriptionId("apartments-rent");
        (await ScalarAsync<string>(stack, "SELECT filter_key FROM harvest_subscriptions WHERE id=@id", ("id", marketplace))).Should().Be("marketplace-listing-v1");
        Guid pipeline = haven.Searches["apartments-rent"].GetProperty("pipelineId").GetGuid();
        Guid tenant = await ScalarAsync<Guid>(stack, "SELECT tenant_id FROM harvest_subscriptions WHERE id=@id", ("id", marketplace));
        Guid unfiltered;
        await using (var scope = stack.Host.Services.CreateAsyncScope())
            unfiltered = (await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().SubscribeAsync(new SubscriptionDeclaration
            {
                TenantId = tenant, ContainerId = haven.ContainerId, PipelineId = pipeline,
            })).ValueOrThrow().Id;
        var tagging = stack.Host.Services.GetRequiredService<IOptions<PostTagsOptions>>().Value;
        tagging.Enabled = false;
        var ad = await SaveAdAsync(stack, haven, "apartments-rent", "IDquiet");
        ad.Post.Base.Tags.Should().BeEmpty("tagging was off when the ad was saved");
        tagging.Enabled = true;

        await using (var scope = stack.Host.Services.CreateAsyncScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<IPostTagsBroker>();
            (await tags.BackfillAsync(new PostTagsBackfillRequest())).ValueOrThrow().Should().Be(1);
            (await tags.ProcessDueAsync()).Should().Be(1);
        }

        await using var read = stack.Host.Services.CreateAsyncScope();
        var stored = (await read.ServiceProvider.GetRequiredService<IPostBroker>().GetPostAsync(haven.ContainerId, ad.Post.Id)).ValueOrThrow();
        stored.Revision.Should().Be(2, "the backfill's rule tags are a revision of the post");
        stored.Base.Tags.Should().HaveCount(3);
        (await EventsAsync(stack, unfiltered, expected: 2)).Select(item => item.Type).Should().Equal("post.new", "post.changed");
        (await EventsAsync(stack, marketplace, expected: 1)).Select(item => item.Type).Should().Equal(["post.new"],
            "Haven reads the listing, so a revision that only moved the tags is not delivered to it");
    }

    [Fact]
    public async Task An_afisha_post_still_queues_its_tagging_call()
    {
        await using var stack = await StartAsync();
        var run = await RunOcharoAsync(stack, ["setup", "--group", "afisha", "--model-processing", "auto", "--run"]);
        var channel = run.GetProperty("subscriptions").EnumerateArray().First();
        Guid pipelineId = channel.GetProperty("pipelineId").GetGuid();
        Guid containerId = run.GetProperty("containerId").GetGuid();
        Guid runId = run.GetProperty("runs").EnumerateArray().First(item => item.GetProperty("pipelineId").GetGuid() == pipelineId).GetProperty("runId").GetGuid();
        await using var scope = stack.Host.Services.CreateAsyncScope();
        var pipeline = (await scope.ServiceProvider.GetRequiredService<IRegistrationBroker>().GetPipelineAsync(pipelineId)).ValueOrThrow();

        var saved = await SaveAsync(scope.ServiceProvider, containerId, pipeline.SourceId, runId, "https://www.afisha.uz/ru/2026/12/01/synthetic-show",
            "synthetic-show", "Moda haftaligi", "Moda haftaligi 12-14 dekabr kunlari bo'ladi.", PostKind.Event);

        saved.Post.Base.Tags.Should().BeEmpty("the model has not tagged it yet");
        (await ScalarAsync<long>(stack, Work, ("post", saved.Post.Id), ("kind", "post-tags"))).Should().Be(1);
        (await ScalarAsync<long>(stack, "SELECT count(*) FROM harvest_post_tags WHERE post_id=@post", ("post", saved.Post.Id))).Should().Be(0);
    }

    private static async Task<Stack> StartAsync()
    {
        var caller = new ScriptedAiModelCaller();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        string database = await SourcePostgres.NewDatabaseAsync();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = database,
            ["Harvest:Operator:ApiKey"] = "hv_" + new string('l', 32),
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            [AiEnabled] = "true",
            ["Ai:Module:RetriesPerModel"] = "0",
            [$"{PostTagsConstants.SectionName}:Enabled"] = "true",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:EntityType"] = "event",
            [$"{PostExtractionConstants.SectionName}:Wanted:0:Domain"] = "style-fashion",
            [$"{PostExtractionConstants.SectionName}:Wanted:1:EntityType"] = "listing",
            [$"{PostExtractionConstants.SectionName}:Wanted:1:Domain"] = "real-estate",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        builder.Services.AddSingleton<IAiModelCaller>(caller);
        var host = builder.Build();
        await host.ConfigureAsync();
        using var draft = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new PromptDraft
        {
            UseCase = "extract-listing", SystemTemplate = "Extract the listing.", UserTemplate = "{{title}}\n{{section}}\n{{attributes}}\n{{text}}", By = "operator",
        }));
        (await PostExtractionVerb.RunAsync(host.Services, ["prompt", "--use-case", "listing", "--file", "-"], draft, TextWriter.Null, TextWriter.Null,
            CancellationToken.None)).Should().Be(0);
        return new Stack(host, caller, database);
    }

    /// <summary>Applies Haven's profile with a model-processing mode and one capture run per search.</summary>
    private static async Task<Haven> SetupHavenAsync(Stack stack, string mode)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await MarketplaceSetupVerb.RunAsync(stack.Host.Services, ["setup", "--profile", "haven", "--model-processing", mode, "--run"],
            output, error, CancellationToken.None);
        exit.Should().Be(0, error.ToString());
        var root = JsonDocument.Parse(output.ToString().TrimEnd().Split('\n')[^1]).RootElement.Clone();
        return new Haven(root.GetProperty("containerId").GetGuid(),
            root.GetProperty("searches").EnumerateArray().ToDictionary(item => item.GetProperty("key").GetString()!, item => item.Clone()),
            root.GetProperty("runs").EnumerateArray().ToDictionary(item => item.GetProperty("pipelineId").GetGuid(), item => item.GetProperty("runId").GetGuid()));
    }

    private static async Task<JsonElement> RunOcharoAsync(Stack stack, string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await OcharoSetupVerb.RunAsync(stack.Host.Services, arguments, output, error, CancellationToken.None);
        exit.Should().Be(0, error.ToString());
        return JsonDocument.Parse(output.ToString().TrimEnd().Split('\n')[^1]).RootElement.Clone();
    }

    /// <summary>Saves an ad through one of Haven's OLX searches, as a capture of that search's queued run. Its title carries the key, so
    /// two ads never read as one post.</summary>
    private static async Task<CaptureSaveResult> SaveAdAsync(Stack stack, Haven haven, string search, string key)
    {
        var row = haven.Searches[search];
        await using var scope = stack.Host.Services.CreateAsyncScope();
        return await SaveAsync(scope.ServiceProvider, haven.ContainerId, row.GetProperty("sourceId").GetGuid(),
            haven.Runs[row.GetProperty("pipelineId").GetGuid()], $"https://www.olx.uz/d/obyavlenie/synthetic-{key}.html", key, $"{Title} {key}", Text,
            PostKind.Listing);
    }

    private static async Task<CaptureSaveResult> SaveAsync(IServiceProvider services, Guid containerId, Guid sourceId, Guid runId, string url, string key,
        string title, string text, PostKind kind)
    {
        var capture = new Capture
        {
            ExternalKey = key, Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = DateTimeOffset.UtcNow,
            Body = Encoding.UTF8.GetBytes($"{title}\n{text}"), ContentProjection = $"{title}\n{text}", HttpStatus = 200, ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = kind, Title = title, Text = text, Language = new PostLanguage { Code = "uz" },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        return (await services.GetRequiredService<IPostBroker>().SaveCaptureAsync(containerId, sourceId, runId, capture, envelope)).ValueOrThrow();
    }

    /// <summary>Pulls a subscription's page, waiting up to five seconds for <paramref name="expected"/> events: a pull reads below the
    /// cluster's oldest open transaction, so a concurrent test can briefly hide a committed event.</summary>
    private static async Task<IReadOnlyList<DeliveryEvent>> EventsAsync(Stack stack, Guid subscription, int expected = 0)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = stack.Host.Services.CreateAsyncScope();
            var page = (await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().PullAsync(subscription)).ValueOrThrow().Data;
            if (page.Count >= expected || attempt == 20)
                return page;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private static async Task<T> ScalarAsync<T>(Stack stack, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(stack.Database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static string ListingAnswer() => JsonSerializer.Serialize(new
    {
        title = "2-room flat for rent",
        categoryPath = new[] { "Ko'chmas mulk", "Ijara" },
        propertyKind = "apartment",
        invalid = (string?)null,
        offerType = "rent",
        price = new { amount = 500m, currency = "usd", period = "month", negotiable = false, evidence = "Narxi 500 USD oyiga" },
        item = new
        {
            propertyType = (string?)null, buildingType = (string?)null, yearBuilt = (int?)null, rooms = (int?)2, layout = (string?)null,
            areaM2 = new { total = (decimal?)null, living = (decimal?)null, kitchen = (decimal?)null }, floor = (int?)null, floors = (int?)null,
            ceilingM = (decimal?)null, bathrooms = new { count = (int?)null, type = (string?)null }, renovation = (string?)null, building = (string?)null,
        },
        furnished = "unknown",
        apartmentAmenities = Array.Empty<string>(),
        nearbyAmenities = Array.Empty<string>(),
        location = new { district = (string?)null, city = "Tashkent", address = (string?)null },
        landmarks = Array.Empty<object>(),
        author = new { kind = "owner" },
        noAgents = false,
        relation = new { role = "announcement", changes = Array.Empty<string>(), status = (string?)null },
        rentalTerms = new
        {
            deposit = new { status = "present", claims = new[] { new { evidence = "Depozit: 500 USD", value = new { amount = 500m, currency = "usd", rentMultiple = (decimal?)null } } } },
            advanceRent = new { status = "unknown", claims = Array.Empty<object>() },
            utilities = new { status = "unknown", claims = Array.Empty<object>() },
            minimumStay = new { status = "unknown", claims = Array.Empty<object>() },
            commission = new { status = "unknown", claims = Array.Empty<object>() },
        },
        acceptedTenants = Array.Empty<string>(),
        excludedTenants = Array.Empty<string>(),
        urgent = false,
        flags = Array.Empty<object>(),
    });

    private sealed record Haven(Guid ContainerId, Dictionary<string, JsonElement> Searches, Dictionary<Guid, Guid> Runs)
    {
        internal Guid SubscriptionId(string search) => Searches[search].GetProperty("subscriptionId").GetGuid();
    }

    private sealed class Stack(WebApplication host, ScriptedAiModelCaller caller, string database) : IAsyncDisposable
    {
        internal WebApplication Host { get; } = host;
        internal ScriptedAiModelCaller Caller { get; } = caller;
        internal string Database { get; } = database;

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            NpgsqlConnection.ClearPool(new NpgsqlConnection(Database));
        }
    }
}
