using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>The two delivery writes besides a post's own ingest, for a source whose entry the instance turned off: an extraction answer
/// (<c>post.extracted</c>) and a selector adoption write no event for a post whose every link is an off source, and both write
/// again once the entry is on.</summary>
[Collection("Post delivery transactions")]
public sealed class SourceCatalogExtractedAndAdoptTests
{
    private static readonly string[] FashionTags = ["type:event", "domain:style-fashion", "topic:fashion-week"];
    private static readonly string[] SportsTags = ["type:event", "domain:sports", "topic:marathon"];
    private static readonly SubscriptionSelector Fashion = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] };

    [Fact]
    public async Task Extracted_ShouldWriteNoEventForAnOffSource_ToAChannelOrASelectorSubscription_AndWriteAgainWhenItsEntryIsOn()
    {
        await using var host = await PostHost.StartAsync();
        var channel = PostHost.Value(await host.Delivery.SubscribeAsync(Channel(host)));
        var selector = PostHost.Value(await host.Delivery.SubscribeAsync(Selector(host, "ocharo-events")));
        var saved = PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            PostStoreTests.Capture(host, "fw", "A fashion week."), Tagged("Fashion week", "A fashion week.", FashionTags)));
        (await host.PullUntilAsync(channel.Id, page => page.Data.Count >= 1)).Data.Should().ContainSingle();
        (await host.PullUntilAsync(selector.Id, page => page.Data.Count >= 1)).Data.Should().ContainSingle();
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);

        await ExtractedAsync(host, saved.Post.Id, ":x1");

        PostHost.Value(await host.Delivery.PullAsync(channel.Id)).Data.Should().ContainSingle("an answer for an off source's post writes no event to a channel");
        PostHost.Value(await host.Delivery.PullAsync(selector.Id)).Data.Should().ContainSingle("nor to a selector subscription");

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        await ExtractedAsync(host, saved.Post.Id, ":x2");

        foreach (var id in new[] { channel.Id, selector.Id })
        {
            var page = await host.PullUntilAsync(id, page => page.Data.Count >= 2);
            page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostExtracted);
        }
    }

    [Fact]
    public async Task Adopt_ShouldWriteNoEventForAnOffSource_NeitherANewNorAnUnmatch_AndWriteWhenItsEntryIsOn()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var old = PostHost.Value(await scope.Delivery.SubscribeAsync(Channel(host)));
        PostHost.Value(await scope.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            PostStoreTests.Capture(host, "fw", "A fashion week."), Tagged("Fashion week", "A fashion week.", FashionTags)));
        PostHost.Value(await scope.Posts.SaveCaptureAsync(host.ContainerId, host.SourceId, null,
            PostStoreTests.Capture(host, "run", "A marathon."), Tagged("A marathon", "A marathon.", SportsTags)));
        var into = PostHost.Value(await scope.Delivery.SubscribeAsync(Selector(host, "adopting") with { Backfill = BackfillMode.None }));
        (await host.PullUntilAsync(old.Id, page => page.Data.Count >= 2)).Data.Should().HaveCount(2, "the channel holds both posts");
        await CatalogSql.TurnOffAsync(host.ConnectionString, host.SourceId);
        var request = new SelectorAdoptRequest { IntoSubscriptionId = into.Id, FromSubscriptionIds = [old.Id] };

        var off = PostHost.Value(await scope.Operations.AdoptAsync(request));

        (off.Considered, off.New, off.Unmatched).Should().Be((2, 0, 0), "adopting writes neither a new nor an unmatch for an off source's post");
        (await Events(host, into.Id)).Should().Be(0);

        await CatalogSql.SetAsync(host.ConnectionString, CatalogSql.Key, true);
        var on = PostHost.Value(await scope.Operations.AdoptAsync(request));

        (on.New, on.Unmatched).Should().Be((1, 1), "with the entry on, the fashion week is adopted and the marathon is unmatched as before");
        (await Events(host, into.Id)).Should().Be(2);
    }

    private static async Task ExtractedAsync(PostHost host, Guid postId, string suffix)
    {
        var post = PostHost.Value(await host.Posts.GetPostAsync(host.ContainerId, postId));
        using var extras = JsonDocument.Parse("""{"price":"50 000"}""");
        await using var connection = new NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await DeliveryEventWriter.AppendExtractedAsync(connection, transaction, post, "extraction", extras.RootElement.Clone(), suffix,
            host.Clock.GetUtcNow().AddMinutes(1), new DeliveryPostFilters([]), CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static Task<long> Events(PostHost host, Guid subscriptionId) =>
        host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", ("id", subscriptionId));

    private static SubscriptionDeclaration Channel(PostHost host) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, Schema = "wow2.event.v1", Backfill = BackfillMode.None,
    };

    private static SubscriptionDeclaration Selector(PostHost host, string name) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, Name = name, Selector = Fashion,
    };

    private static PostEnvelope Tagged(string title, string text, string[] tags) => PostStoreTests.Envelope(title, text) with
    {
        Tags = [.. tags.Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel })],
    };

    /// <summary>Opens the module again over the host's database with the subscription operations broker.</summary>
    private static OperationsScope Open(PostHost host)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(host.Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = host.ConnectionString);
        services.AddHarvestPosts().AddHarvestDelivery().AddHarvestSubscriptionOperations();
        var provider = services.BuildServiceProvider(validateScopes: true);
        return new OperationsScope(provider, provider.CreateAsyncScope());
    }

    private sealed class OperationsScope(ServiceProvider provider, AsyncServiceScope scope) : IAsyncDisposable
    {
        internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
        internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        internal ISubscriptionOperationsBroker Operations => scope.ServiceProvider.GetRequiredService<ISubscriptionOperationsBroker>();

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }
}
