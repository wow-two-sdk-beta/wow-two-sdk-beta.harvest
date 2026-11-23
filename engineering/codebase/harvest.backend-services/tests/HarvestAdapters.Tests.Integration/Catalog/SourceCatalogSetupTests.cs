using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Integration.Catalog;

/// <summary>The Ocharo and marketplace catalogs over a real store: setup stamps <c>catalog_key</c> and turns its entries on, a bulk
/// setup keeps an off entry off, the boot reconcile keeps existing sources on, and turning a <c>new</c> entry on sets it up with
/// schedules and model processing off.</summary>
[Collection(SourceDeliveryCollection.Name)]
public sealed class SourceCatalogSetupTests
{
    [Fact]
    public async Task OcharoSetup_ShouldStampEverySourceAndTurnOnEveryEntry_AndARepeatChangesNoRecord()
    {
        await using var host = await StartAsync();
        var setup = Value(await host.Scoped<OcharoSourceProvisioner>().EnsureAsync());

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources WHERE catalog_key IS NULL")).Should().Be(0);
        (await host.ScalarAsync<string>("SELECT catalog_key FROM harvest_sources WHERE handle='www.afisha.uz'")).Should().Be("ocharo.afisha");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_catalog_sources WHERE turned_on AND changed_by='setup'"))
            .Should().Be(setup.Sources.Select(row => row.GroupId).Distinct().Count(), "one on record per entry setup set up");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_catalog_sources")).Should().BeLessThan(57, "an entry nothing of which setup ran has no record");
        var records = await host.ScalarAsync<string>("SELECT string_agg(key || changed_at::text, ',' ORDER BY key) FROM harvest_catalog_sources");

        Value(await host.Scoped<OcharoSourceProvisioner>().EnsureAsync());

        (await host.ScalarAsync<string>("SELECT string_agg(key || changed_at::text, ',' ORDER BY key) FROM harvest_catalog_sources")).Should().Be(records);
    }

    [Fact]
    public async Task OcharoSetup_ShouldSkipAnOffGroupWhenItNamesNone_AndTurnItOnWhenItNamesIt()
    {
        await using var host = await StartAsync();
        await host.Scoped<SourceCatalogStore>().SetAsync("ocharo.afisha", false, "subject-1");

        var bulk = Value(await host.Scoped<OcharoSourceProvisioner>().EnsureAsync());

        bulk.Skipped.Should().Equal("ocharo.afisha");
        bulk.Sources.Should().NotContain(row => row.GroupId == "afisha");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources WHERE handle='www.afisha.uz'")).Should().Be(0);
        (await host.ScalarAsync<bool>("SELECT turned_on FROM harvest_catalog_sources WHERE key='ocharo.afisha'")).Should().BeFalse("a bulk setup never turns it back on");

        var named = Value(await host.Scoped<OcharoSourceProvisioner>().EnsureAsync(groupIds: ["afisha"]));

        named.Skipped.Should().BeEmpty();
        named.Sources.Should().NotBeEmpty();
        (await host.ScalarAsync<string>("SELECT catalog_key FROM harvest_sources WHERE handle='www.afisha.uz'")).Should().Be("ocharo.afisha");
        (await host.ScalarAsync<string>("SELECT turned_on || ':' || changed_by FROM harvest_catalog_sources WHERE key='ocharo.afisha'")).Should().Be("true:setup");
    }

    [Fact]
    public async Task BootReconcile_ShouldStampExistingSourcesAndRecordThemOn_LeaveAnOffRecordOff_AndChangeNothingOnARerun()
    {
        await using var host = await StartAsync();
        var scope = host.Scope;
        var before = new OcharoSourceProvisioner(scope.ServiceProvider.GetRequiredService<IRegistrationBroker>(), scope.ServiceProvider.GetRequiredService<IDeliveryBroker>());
        Value(await before.EnsureAsync());
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources WHERE catalog_key IS NOT NULL")).Should().Be(0, "a source set up before the catalog carries no key");
        await host.Scoped<SourceCatalogStore>().SetAsync("ocharo.anons", false, "subject-1");

        await host.Provider.ReconcileHarvestSourceCatalogAsync(CancellationToken.None);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources WHERE catalog_key IS NULL")).Should().Be(0, "every collectible channel's host in the tenant's container is matched");
        (await host.ScalarAsync<string>("SELECT catalog_key FROM harvest_sources WHERE handle='www.afisha.uz'")).Should().Be("ocharo.afisha");
        (await host.ScalarAsync<string>("SELECT turned_on || ':' || changed_by FROM harvest_catalog_sources WHERE key='ocharo.afisha'")).Should().Be("true:existing");
        (await host.ScalarAsync<string>("SELECT turned_on || ':' || changed_by FROM harvest_catalog_sources WHERE key='ocharo.anons'")).Should().Be("false:subject-1",
            "an existing record never changes");
        var state = await host.ScalarAsync<string>("""
            SELECT (SELECT string_agg(key || turned_on || changed_by || changed_at::text, ',' ORDER BY key) FROM harvest_catalog_sources)
                || '|' || (SELECT string_agg(handle || coalesce(catalog_key,'-'), ',' ORDER BY id) FROM harvest_sources)
            """);

        await host.Provider.ReconcileHarvestSourceCatalogAsync(CancellationToken.None);

        (await host.ScalarAsync<string>("""
            SELECT (SELECT string_agg(key || turned_on || changed_by || changed_at::text, ',' ORDER BY key) FROM harvest_catalog_sources)
                || '|' || (SELECT string_agg(handle || coalesce(catalog_key,'-'), ',' ORDER BY id) FROM harvest_sources)
            """)).Should().Be(state, "a rerun changes nothing");
        var list = await host.Scoped<SourceCatalogBroker>().ListAsync();
        list.Single(entry => entry.Key == "ocharo.anons").State.Should().Be(CatalogState.Off);
        list.Single(entry => entry.Key == "ocharo.afisha").State.Should().Be(CatalogState.On);
    }

    [Fact]
    public async Task TurningOnANewOcharoEntry_ShouldSetItUpWithSchedulesAndModelProcessingOff()
    {
        await using var host = await StartAsync();
        var broker = host.Scoped<SourceCatalogBroker>();
        (await broker.ListAsync()).Should().HaveCount(63 + 4).And.OnlyContain(entry => entry.State == CatalogState.New, "a fresh instance starts with every entry new");

        var on = Value(await broker.SwitchAsync("ocharo.afisha", true, "subject-1"));

        on.State.Should().Be(CatalogState.On);
        (on.ChangedBy, on.Sources.Count).Should().Be(("subject-1", 2));
        var registry = host.Scoped<IRegistrationBroker>();
        Guid containerId = on.Sources.Select(source => source.ContainerId).Distinct().Single();
        var sources = Value(await registry.ListSourcesAsync(containerId));
        sources.Select(source => source.Handle).Should().BeEquivalentTo(new[] { "www.afisha.uz", "afishauzb" });
        sources.Should().OnlyContain(source => source.CatalogKey == "ocharo.afisha" && source.TurnedOn
            && source.ModelProcessing == ModelProcessingMode.Off);
        var pipelines = Value(await registry.ListPipelinesAsync(containerId));
        pipelines.Should().NotBeEmpty();
        pipelines.SelectMany(pipeline => pipeline.Schedules).Should().OnlyContain(schedule => !schedule.Enabled, "the source reads Manual until its schedules are switched on");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources")).Should().Be(2, "only the entry's own hosts are set up");
    }

    [Fact]
    public async Task TurningOnANewMarketplaceEntry_ShouldSetUpOnlyThatSiteInEveryProfileThatListsIt()
    {
        await using var host = await StartAsync();
        var broker = host.Scoped<SourceCatalogBroker>();

        var on = Value(await broker.SwitchAsync("marketplace.olx-uz", true, "subject-2"));

        on.State.Should().Be(CatalogState.On);
        on.Sources.Should().ContainSingle();
        var registry = host.Scoped<IRegistrationBroker>();
        var source = Value(await registry.GetSourceAsync(on.Sources[0].SourceId));
        (source.Handle, source.CatalogKey, source.ModelProcessing).Should().Be(("www.olx.uz", "marketplace.olx-uz", ModelProcessingMode.Off));
        var pipelines = Value(await registry.ListPipelinesAsync(source.ContainerId));
        pipelines.Should().HaveCount(3, "OLX's three Haven searches, none of the other sites'")
            .And.OnlyContain(pipeline => pipeline.SourceId == source.Id && pipeline.Schedules.All(schedule => !schedule.Enabled));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_api_keys")).Should().Be(0, "a catalog set-up issues no key");
    }

    [Fact]
    public async Task MarketplaceSetup_ShouldStampAndTurnOnItsSites_AndSkipAnOffSiteForAProfile()
    {
        await using var host = await StartAsync();
        await host.Scoped<SourceCatalogStore>().SetAsync("marketplace.olx-uz", false, "subject-1");

        var setup = Value(await host.Scoped<MarketplaceProvisioner>().EnsureAsync(MarketplaceProfiles.Haven));

        setup.Skipped.Should().Equal("marketplace.olx-uz");
        setup.Searches.Should().NotContain(search => search.Key.EndsWith("-rent", StringComparison.Ordinal) && search.Url.Contains("olx.uz", StringComparison.Ordinal));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources WHERE handle='www.olx.uz'")).Should().Be(0);
        (await host.ScalarAsync<string>("SELECT string_agg(catalog_key, ',' ORDER BY catalog_key) FROM harvest_sources"))
            .Should().Be("marketplace.birbir-uz,marketplace.birbir-uz,marketplace.domtut-uz,marketplace.joymee-uz");
        (await host.ScalarAsync<string>("SELECT string_agg(key || ':' || turned_on || ':' || changed_by, ',' ORDER BY key) FROM harvest_catalog_sources"))
            .Should().Be("marketplace.birbir-uz:true:setup,marketplace.domtut-uz:true:setup,marketplace.joymee-uz:true:setup,marketplace.olx-uz:false:subject-1");
    }

    private static async Task<CatalogHost> StartAsync()
    {
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddHarvestSources();
        services.AddOcharoEventSources();
        services.AddHarvestListingSources();
        var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("catalog-fixture");
        return new CatalogHost(provider, provider.CreateAsyncScope(), connectionString);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class CatalogHost(ServiceProvider provider, AsyncServiceScope scope, string connectionString) : IAsyncDisposable
    {
        internal ServiceProvider Provider { get; } = provider;

        internal AsyncServiceScope Scope { get; } = scope;

        internal T Scoped<T>() where T : notnull => Scope.ServiceProvider.GetRequiredService<T>();

        internal async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await Provider.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
        }
    }
}
