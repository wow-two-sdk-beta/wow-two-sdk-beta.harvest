using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestAdapters.Tests.Integration.Catalog;

/// <summary>A marketplace import for a site whose entry the instance turned off: the importer's capture is refused with
/// <c>source-turned-off</c> and writes no capture, post or link; turning the entry on lets the same import through.</summary>
[Collection(SourceDeliveryCollection.Name)]
public sealed class SourceCatalogImportTests
{
    private const string Search = "domtut-apartments-rent";

    private static readonly MarketplaceListingImport Listing = new("4711", "https://domtut.uz/ad/4711", "Two-room flat in Chilanzar",
        "A bright two-room flat.", "500 USD", "Tashkent", "Chilanzar");

    [Fact]
    public async Task Import_ShouldBeRefusedAndWriteNothing_ForAnOffSite_AndGoThroughWhenItsEntryIsOn()
    {
        await using var host = await StartAsync();
        Value(await host.Scoped<MarketplaceProvisioner>().EnsureAsync(MarketplaceProfiles.Haven));
        await host.Scoped<SourceCatalogStore>().SetAsync("marketplace.domtut-uz", false, "subject-1");

        var refused = await host.Scoped<MarketplaceListingImporter>().ImportAsync(MarketplaceProfiles.Haven, Search, Listing, default);

        var failure = refused.Should().BeOfType<Result<CaptureSaveResult>.Failure>().Subject;
        failure.Error.Type.Should().Be(AppErrorType.Forbidden);
        failure.Error.Message.Should().StartWith("source-turned-off");
        (await host.ScalarAsync<string>("SELECT (SELECT count(*) FROM harvest_captures) || ',' || (SELECT count(*) FROM harvest_posts) || ',' || (SELECT count(*) FROM harvest_post_links)"))
            .Should().Be("0,0,0", "a refused import stores nothing");

        await host.Scoped<SourceCatalogStore>().SetAsync("marketplace.domtut-uz", true, "subject-1");

        Value(await host.Scoped<MarketplaceListingImporter>().ImportAsync(MarketplaceProfiles.Haven, Search, Listing, default));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_posts")).Should().Be(1, "with the entry on, the same import is stored");
    }

    private static async Task<ImportHost> StartAsync()
    {
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddHarvestSources();
        services.AddHarvestListingSources();
        var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("catalog-import-fixture");
        return new ImportHost(provider, provider.CreateAsyncScope(), connectionString);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class ImportHost(ServiceProvider provider, AsyncServiceScope scope, string connectionString) : IAsyncDisposable
    {
        internal T Scoped<T>() where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

        internal async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            NpgsqlConnection.ClearAllPools();
        }
    }
}
