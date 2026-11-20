using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace Harvest.Tests.Integration.PostExtraction;

/// <summary>An OLX ad through both passes over PostgreSQL with a scripted model: the phone never reaches a prompt, the listing prompt
/// receives the ad's attributes, its section key and its seller category, an eval subject resolves the same inputs, and a quote
/// copied from the attributes backs a rental term. The ad, its numbers and its attributes are invented.</summary>
public sealed class ListingExtractionTests
{
    private const string Title = "2-комнатная квартира, Чиланзар";
    private const string Text = "Сдается 2-комнатная квартира. Цена 500 USD в месяц. Депозит: 500 USD. Звоните +998 90 123 45 67 или 93 765 43 21.";
    private const string System = "Extract the listing.";
    private const string User = "{{title}}|{{section}}|{{seller}}|{{sourceCategory}}\n{{attributes}}\n{{text}}";
    private const string SearchUrl = "https://www.olx.uz/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/?search%5Border%5D=created_at%3Adesc";
    private const string PipelineName = "OLX · Tashkent apartments, long-term rent · Haven";

    [Fact]
    public async Task Pass_ShouldSendTheScrubbedAd_WithItsAttributesSectionAndSeller_AndStoreAnAbsentCommissionQuotedFromTheAttributes()
    {
        await using var host = await StartAsync();
        var saved = await SaveAdAsync(host);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "listing", domains: [new { node = "real-estate", primary = true }])),
            ScriptedAiModelCaller.Answer(Answer()));

        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await Extraction(host).ProcessDueAsync()).Should().Be(1);

        var tagging = host.Caller.Requests[0].User;
        tagging.Should().Contain("[phone]").And.NotContain("123 45 67").And.NotContain("765 43 21").And.NotContain("+998");
        var sent = host.Caller.Requests[1].User;
        sent.Should().Be($"{Title}|apartments-rent|business|Недвижимость › Аренда\n"
            + "Бизнес\nКомиссионные: Нет\nКоличество комнат: 2\n"
            + "Сдается 2-комнатная квартира. Цена 500 USD в месяц. Депозит: 500 USD. Звоните [phone] или [phone].");
        host.Caller.Requests.Select(request => request.User).Should().NotContain(user => user.Contains("Seller Name") || user.Contains("123"));

        using var answer = JsonDocument.Parse(await host.ScalarAsync<string>(
            "SELECT answer::text FROM harvest_post_extractions WHERE post_id=@post AND use_case='extract-listing' AND status='done'", ("post", saved.Post.Id)));
        var commission = answer.RootElement.GetProperty("rentalTerms").GetProperty("commission");
        commission.GetProperty("status").GetString().Should().Be("absent");
        commission.GetProperty("claims")[0].GetProperty("evidence").GetString().Should().Be("Комиссионные: Нет");
        answer.RootElement.GetProperty("rentalTerms").GetProperty("deposit").GetProperty("claims")[0].GetProperty("value")
            .GetProperty("currency").GetString().Should().Be("usd");
        answer.RootElement.GetProperty("item").GetProperty("areaM2").GetProperty("total").ValueKind.Should().Be(JsonValueKind.Null);
        answer.RootElement.GetProperty("flags").GetArrayLength().Should().Be(0);
        (await host.ScalarAsync<string>("SELECT row_to_json(e)::text FROM harvest_post_extractions e WHERE post_id=@post AND use_case='extract-listing'",
            ("post", saved.Post.Id))).Should().NotContain("+998").And.NotContain("123 45 67");
    }

    [Fact]
    public async Task SubjectSource_ShouldResolveTheListingInputsExactlyAsThePassBuildsThem_AndNothingMoreForAnEvent()
    {
        await using var host = await StartAsync();
        var saved = await SaveAdAsync(host);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "listing", domains: [new { node = "real-estate", primary = true }])),
            ScriptedAiModelCaller.Answer(Answer()));
        await host.Tags.ProcessDueAsync();
        await Extraction(host).ProcessDueAsync();
        var sent = host.Caller.Requests.Last().User;
        var source = host.Scoped.GetServices<IEvalSubjectSource>().Single(candidate => candidate.Prefix == "harvest:post:");

        var resolved = await source.ResolveAsync(PostExtractionConstants.ListingUseCase, $"harvest:post:{saved.Post.Id:D}");

        resolved.Should().NotBeNull();
        Render(User, resolved!).Should().Be(sent);
        resolved!["section"].Should().Be("apartments-rent");
        resolved["seller"].Should().Be("business");
        resolved["attributes"].Should().Be("Бизнес\nКомиссионные: Нет\nКоличество комнат: 2", "the stored envelope keeps its keys in the database order");
        resolved["text"].Should().Contain("[phone]").And.NotContain("123 45 67");
        resolved.Keys.Should().BeEquivalentTo([.. PostExtractionDefinitions.VariablesOf(PostExtractionConstants.ListingUseCase)]);
        (await source.ResolveAsync(PostExtractionConstants.EventUseCase, $"harvest:post:{saved.Post.Id:D}")).Should().BeNull("the post is tagged a listing");
        var tags = await source.ResolveAsync("post-tags", $"harvest:post:{saved.Post.Id:D}");
        tags!["text"].Should().Contain("[phone]").And.NotContain("123 45 67");
    }

    [Fact]
    public async Task Section_ShouldFallBackToThePipelineName_WhenNoAdapterCatalogNamesIt()
    {
        await using var host = await StartAsync(withCatalog: false);
        var saved = await SaveAdAsync(host);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(entityType: "listing", domains: [new { node = "real-estate", primary = true }])),
            ScriptedAiModelCaller.Answer(Answer()));
        await host.Tags.ProcessDueAsync();
        await Extraction(host).ProcessDueAsync();

        host.Caller.Requests[1].User.Should().StartWith($"{Title}|{PipelineName}|business|");
        saved.Post.Id.Should().NotBe(Guid.Empty);
    }

    private static async Task<TagsHost> StartAsync(bool withCatalog = true)
    {
        var host = await TagsHost.StartAsync(enabled: true, configure: services =>
        {
            services.AddHarvestPostExtraction().Configure(options => options.Wanted = [PostExtractionTests.Want("listing", "real-estate")]);
            if (withCatalog)
                services.AddSingleton<ISourceAdapter>(new OlxCatalog());
        });
        var prompts = host.Scoped.GetRequiredService<IPromptsBroker>();
        foreach (var useCase in PostExtractionConstants.UseCases)
        {
            (await prompts.PutUseCaseAsync(new UseCaseModel
            {
                Key = useCase, PromptMode = PromptMode.Versioned, OutputSchema = PostExtractionDefinitions.OutputSchema(useCase), KeepOutputs = true,
            })).ValueOrThrow();
            (await prompts.SavePromptAsync(new PromptDraft { UseCase = useCase, SystemTemplate = System, UserTemplate = User, By = "test" })).ValueOrThrow();
        }
        return host;
    }

    /// <summary>Saves the OLX ad through a pipeline run on an OLX source, so the post has its section.</summary>
    private static async Task<CaptureSaveResult> SaveAdAsync(TagsHost host)
    {
        var olx = await host.SourceAsync("olx.uz");
        var pipeline = (await host.Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = host.ContainerId, SourceId = olx, Scope = new PipelineScope { Url = SearchUrl }, Name = PipelineName,
        })).ValueOrThrow();
        var run = Guid.CreateVersion7(host.Clock.GetUtcNow());
        await host.ScalarAsync<long>("""
            WITH run AS (
                INSERT INTO harvest_runs(id,container_id,pipeline_id,kind,trigger,scheduled_for,status,queued_at,started_at)
                SELECT @run,container_id,id,'capture','manual',@now,'completed',@now,@now FROM harvest_pipelines WHERE id=@pipeline RETURNING 1)
            SELECT count(*) FROM run
            """, ("run", run), ("now", host.Clock.GetUtcNow()), ("pipeline", pipeline.Id));
        var url = "https://www.olx.uz/d/obyavlenie/synthetic-ad-IDabcd.html";
        var capture = new Capture
        {
            ExternalKey = "IDabcd", Url = url, RequestUrl = url, FinalUrl = url, CapturedAtUtc = host.Clock.GetUtcNow(),
            Body = Encoding.UTF8.GetBytes($"{Title}\n{Text}"), ContentProjection = $"{Title}\n{Text}", HttpStatus = 200, ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Listing, Title = Title, Text = Text, Language = new PostLanguage { Code = "ru" },
            SourceTags = ["Недвижимость", "Аренда"],
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
            Extras = new Dictionary<string, JsonElement>
            {
                ["source"] = JsonSerializer.SerializeToElement(new
                {
                    schema = "olx.ad.v1", parserVersion = "olx-html@2",
                    fields = new Dictionary<string, object?>
                    {
                        ["externalId"] = "IDabcd", ["title"] = Title, ["sellerName"] = "Seller Name",
                        ["attributes"] = new Dictionary<string, string> { ["Количество комнат"] = "2", ["Комиссионные"] = "Нет", ["Бизнес"] = "Бизнес" },
                    },
                }),
            },
        };
        return (await host.Posts.SaveCaptureAsync(host.ContainerId, olx, run, capture, envelope)).ValueOrThrow();
    }

    private static string Answer() => JsonSerializer.Serialize(new
    {
        title = "2-room flat for rent in Chilanzar",
        categoryPath = new[] { "Недвижимость", "Аренда" },
        propertyKind = "apartment",
        invalid = (string?)null,
        offerType = "rent",
        price = new { amount = 500m, currency = "usd", period = "month", negotiable = false, evidence = "Цена 500 USD в месяц" },
        item = new
        {
            propertyType = (string?)null, buildingType = (string?)null, yearBuilt = (int?)null, rooms = (int?)2, layout = (string?)null,
            areaM2 = new { total = (decimal?)null, living = (decimal?)null, kitchen = (decimal?)null }, floor = (int?)null, floors = (int?)null,
            ceilingM = (decimal?)null, bathrooms = new { count = (int?)null, type = (string?)null }, renovation = (string?)null, building = (string?)null,
        },
        furnished = "unknown",
        apartmentAmenities = Array.Empty<string>(),
        nearbyAmenities = Array.Empty<string>(),
        location = new { district = "Chilanzar", city = "Tashkent", address = (string?)null },
        landmarks = Array.Empty<object>(),
        author = new { kind = "agency" },
        noAgents = false,
        relation = new { role = "announcement", changes = Array.Empty<string>(), status = (string?)null },
        rentalTerms = new
        {
            deposit = new { status = "present", claims = new[] { new { evidence = "Депозит: 500 USD", value = new { amount = 500m, currency = "usd", rentMultiple = (decimal?)null } } } },
            advanceRent = new { status = "unknown", claims = Array.Empty<object>() },
            utilities = new { status = "unknown", claims = Array.Empty<object>() },
            minimumStay = new { status = "unknown", claims = Array.Empty<object>() },
            commission = new { status = "absent", claims = new[] { new { evidence = "Комиссионные: Нет", value = (object?)null } } },
        },
        acceptedTenants = Array.Empty<string>(),
        excludedTenants = Array.Empty<string>(),
        urgent = false,
        flags = Array.Empty<object>(),
    });

    private static IPostExtractionBroker Extraction(TagsHost host) => PostExtractionTests.Extraction(host);

    private static string Render(string template, IReadOnlyDictionary<string, string?> variables) =>
        Regex.Replace(template, @"\{\{\s*(?<name>[A-Za-z0-9_.\-]+)\s*\}\}", match => variables[match.Groups["name"].Value] ?? "");

    /// <summary>Stands in for the OLX adapter: it serves <c>olx.uz</c> and declares its three catalog sections.</summary>
    private sealed class OlxCatalog : ISourceAdapter, ISourceSectionCatalog
    {
        public string Key => "olx-uz";

        public SourceFamily Family => SourceFamily.Site;

        public bool Serves(SourceModel source) => source.Family == Family && source.Handle == "olx.uz";

        public AdapterDescriptor Describe(SourceModel source) => new()
        {
            SourceUrl = "https://www.olx.uz/", HostKey = "www.olx.uz", Steps = [], Plans = new Dictionary<WorkKind, IReadOnlyList<string>>(),
        };

        public IReadOnlyList<SourceSectionDefinition> Sections(SourceModel source) =>
        [
            new("Tashkent long-term apartments", SearchUrl, "und") { Key = "apartments-rent", RestrictToPath = false, RegisterPassivePipeline = false },
            new("Tashkent long-term houses", "https://www.olx.uz/nedvizhimost/doma/arenda-dolgosrochnaya/tashkent/?search%5Border%5D=created_at%3Adesc", "und")
                { Key = "houses-rent", RestrictToPath = false, RegisterPassivePipeline = false },
        ];
    }
}
