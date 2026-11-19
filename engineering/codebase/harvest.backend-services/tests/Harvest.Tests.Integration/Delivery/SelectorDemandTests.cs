using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace Harvest.Tests.Integration.Delivery;

/// <summary>Layer 2 extraction demand from active selector subscriptions: the pairs a selector adds to the configured list,
/// how long they are cached, and the extractions a created or changed selector queues for its new pairs.</summary>
[Collection("Post delivery transactions")]
public sealed class SelectorDemandTests
{
    private const string Title = "Tashkent Fashion Week";
    private const string Text = "Moda haftaligi 12-14 dekabr kunlari Uzexpocentre'da bo'ladi.";
    private const string Work = "SELECT count(*) FROM harvest_post_work WHERE post_id=@post AND kind=@kind";

    private static readonly SubscriptionSelector Fashion = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] };
    private static readonly SubscriptionSelector Sports = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "sports" }] };

    [Fact]
    public async Task Demand_ShouldUniteTheConfigListWithActiveSelectors()
    {
        await using var host = await PostHost.StartAsync();
        await using var demand = Open(host, new PostExtractionWant { EntityType = "listing", Domain = "real-estate" });
        (await demand.WantedAsync()).Should().Equal(Pairs("listing:real-estate"), "no selector wants anything yet");

        await SubscribeAsync(host, "beauty-less", new SubscriptionSelector
        {
            EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] }],
            Topics = ["textile"], ExcludeDomains = ["sports"],
        });
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await SubscribeAsync(host, "every-sport", new SubscriptionSelector { Domains = [new SelectorDomain { Key = "sports" }, new SelectorDomain { Key = "real-estate" }] });
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await SubscribeAsync(host, "topics-only", new SubscriptionSelector { Topics = ["textile"] });
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        await SubscribeAsync(host, "again", Fashion);
        demand.Invalidate();

        (await demand.WantedAsync()).Should().Equal(Pairs("listing:real-estate", "event:style-fashion", "event:sports", "event:real-estate", "listing:sports"),
            "the config list first, then each active selector's domain includes crossed with its types, once each; except lists, topics and exclusions add nothing");
    }

    [Fact]
    public async Task Demand_ShouldDropAnArchivedSelector_AfterInvalidate()
    {
        await using var host = await PostHost.StartAsync();
        await using var demand = Open(host);
        var subscription = await SubscribeAsync(host, "ocharo-events", Fashion);
        demand.Invalidate();
        (await demand.WantedAsync()).Should().Equal(Pairs("event:style-fashion"));

        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended));
        (await demand.WantedAsync()).Should().Equal(Pairs("event:style-fashion"), "the pairs are cached for ten seconds");
        demand.Invalidate();
        (await demand.WantedAsync()).Should().BeEmpty("a suspended selector wants nothing");

        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Active));
        demand.Invalidate();
        (await demand.WantedAsync()).Should().Equal(Pairs("event:style-fashion"));
        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Archived));
        demand.Invalidate();
        (await demand.WantedAsync()).Should().BeEmpty("an archived selector wants nothing");
    }

    [Fact]
    public async Task Demand_ShouldReadAgain_WhenTheCacheExpires()
    {
        await using var host = await PostHost.StartAsync();
        await using var demand = Open(host);
        (await demand.WantedAsync()).Should().BeEmpty();

        await SubscribeAsync(host, "ocharo-events", Fashion);
        (await demand.WantedAsync()).Should().BeEmpty("the empty answer is still cached");
        host.Clock.Advance(TimeSpan.FromSeconds(9));
        (await demand.WantedAsync()).Should().BeEmpty();
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        (await demand.WantedAsync()).Should().Equal(Pairs("event:style-fashion"), "past ten seconds the selectors are read again");
    }

    [Fact]
    public void AddHarvestSelectorDemand_ShouldWin_InEitherOrderAgainstAddHarvestPostExtraction()
    {
        var before = new ServiceCollection();
        before.AddHarvestSelectorDemand();
        before.AddHarvestPostExtraction();
        var after = new ServiceCollection();
        after.AddHarvestPostExtraction();
        after.AddHarvestSelectorDemand();
        after.AddHarvestSelectorDemand();

        foreach (var services in new[] { before, after })
        {
            services.Where(descriptor => descriptor.ServiceType == typeof(IPostExtractionDemand)).Should().ContainSingle()
                .Which.ImplementationType.Should().Be(typeof(SelectorPostExtractionDemand));
            services.Where(descriptor => descriptor.ServiceType == typeof(ISelectorChangeHook)).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task SelectorChange_ShouldQueueExtraction_ForItsNewPairs()
    {
        await using var host = await StartExtractingAsync();
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/high-fashion", primary = true }])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(0, "no pair is wanted before a selector names one");

        PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "ocharo-events", Selector = Fashion,
        }));

        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(1, "the new selector's pair queues the tagged post at once");
        (await WorkAsync(host, saved.Post.Id, "extract-listing")).Should().Be(0);
    }

    [Fact]
    public async Task SelectorChange_ShouldQueueExtraction_WhenASetSelectorAddsAPairTheStoredPostsMatch()
    {
        await using var host = await StartExtractingAsync();
        var saved = await host.SaveAsync("fw", Title, Text);
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/high-fashion", primary = true }])));
        await host.Tags.ProcessDueAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "ocharo-events", Selector = Sports,
        }));
        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(0, "a sports selector wants no fashion extraction");

        PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Fashion }));

        (await WorkAsync(host, saved.Post.Id, "extract-event")).Should().Be(1);
    }

    [Fact]
    public async Task Hook_ShouldStayIdle_WhenTheHostDoesNotExtract()
    {
        await using var host = await PostHost.StartAsync();
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(host.Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = host.ConnectionString);
        services.AddHarvestPosts().AddHarvestDelivery().AddHarvestSelectorDemand();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();

        var subscription = PostHost.Value(await delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "ocharo-events", Selector = Fashion,
        }));
        var demand = provider.GetRequiredService<IPostExtractionDemand>();
        (await demand.WantedAsync()).Should().Equal(Pairs("event:style-fashion"));
        PostHost.Value(await delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports }));

        (await demand.WantedAsync()).Should().Equal(Pairs("event:sports"),
            "the hook refreshed the cached demand at once, and found no extraction broker to queue with");
    }

    private static async Task<SubscriptionModel> SubscribeAsync(PostHost host, string name, SubscriptionSelector selector) =>
        PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = name, Selector = selector, Backfill = BackfillMode.None,
        }));

    private static PostExtractionWant[] Pairs(params string[] pairs) =>
        [.. pairs.Select(pair => pair.Split(':')).Select(parts => new PostExtractionWant { EntityType = parts[0], Domain = parts[1] })];

    /// <summary>Opens the selector demand over the host's database with its own cache, beside a module that carries no hook.</summary>
    private static DemandScope Open(PostHost host, params PostExtractionWant[] wanted)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(host.Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = host.ConnectionString);
        services.AddHarvestSelectorDemand();
        services.Configure<PostExtractionOptions>(options => options.Wanted = [.. wanted]);
        return new DemandScope(services.BuildServiceProvider(validateScopes: true));
    }

    private sealed class DemandScope(ServiceProvider provider) : IAsyncDisposable
    {
        private SelectorPostExtractionDemand Demand => (SelectorPostExtractionDemand)provider.GetRequiredService<IPostExtractionDemand>();

        internal ValueTask<IReadOnlyList<PostExtractionWant>> WantedAsync() => Demand.WantedAsync();
        internal void Invalidate() => Demand.Invalidate();
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    /// <summary>Starts the tagging host with the extraction pass composed, its demand the selector's and no configured pair,
    /// and the extraction use cases and prompts registered as the extraction tests register them.</summary>
    private static async Task<TagsHost> StartExtractingAsync()
    {
        var host = await TagsHost.StartAsync(configure: services =>
        {
            services.AddHarvestPostExtraction();
            services.AddHarvestSelectorDemand();
        });
        var prompts = host.Scoped.GetRequiredService<IPromptsBroker>();
        foreach (var useCase in PostExtractionConstants.UseCases)
        {
            (await prompts.PutUseCaseAsync(new UseCaseModel
            {
                Key = useCase, PromptMode = PromptMode.Versioned, OutputSchema = PostExtractionDefinitions.OutputSchema(useCase), KeepOutputs = true,
            })).ValueOrThrow();
            (await prompts.SavePromptAsync(new PromptDraft { UseCase = useCase, SystemTemplate = "Extract.", UserTemplate = "{{title}}\n{{text}}", By = "test" }))
                .ValueOrThrow();
        }
        return host;
    }

    private static Task<long> WorkAsync(TagsHost host, Guid postId, string kind) => host.ScalarAsync<long>(Work, ("post", postId), ("kind", kind));
}
