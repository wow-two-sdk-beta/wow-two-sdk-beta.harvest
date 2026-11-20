using AwesomeAssertions;
using Harvest.Tests.Integration.Engine;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>The catalog broker over PostgreSQL: the list merges entries with records and stamped sources, a switch follows the
/// spec's states, and the boot reconcile keeps existing sources on.</summary>
public sealed class SourceCatalogBrokerTests
{
    private const string Afisha = "test.afisha", Anons = "test.anons", Caex = "test.caex";

    [Fact]
    public async Task List_ShouldBeEmpty_WhenNoCatalogIsRegistered()
    {
        await using var host = await EngineHost.StartAsync(services => services.AddHarvestSources());
        using var scope = host.Services.CreateScope();

        (await scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>().ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task List_ShouldMergeEntriesWithRecordsAndStampedSources_AsNewOnAndOff()
    {
        var catalog = Catalog();
        await using var host = await StartAsync(catalog);
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var stamped = await SourceOfAsync(scope, registration.PipelineId);
        await host.ExecuteAsync("UPDATE harvest_sources SET catalog_key=@key WHERE id=@id", ("key", Afisha), ("id", stamped));
        await CatalogSql.SetAsync(host.ConnectionString, Anons, false);
        var broker = scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>();

        var list = await broker.ListAsync();

        list.Select(entry => (entry.Key, entry.State)).Should().Equal((Afisha, CatalogState.On), (Anons, CatalogState.Off), (Caex, CatalogState.New));
        var afisha = list[0];
        afisha.ChangedAtUtc.Should().BeNull("a source set up without a record has none to show");
        afisha.Sources.Should().ContainSingle().Which.Should().Match<CatalogSourceRef>(item => item.SourceId == stamped
            && item.ContainerId == registration.ContainerId && item.Name == "mstar.uz");
        list[1].Should().Match<CatalogSourceModel>(entry => entry.ChangedBy == "test" && entry.ChangedAtUtc != null && entry.Sources.Count == 0);
        list[2].Should().Match<CatalogSourceModel>(entry => entry.ChangedAtUtc == null && entry.ChangedBy == null && entry.Sources.Count == 0);
    }

    [Fact]
    public async Task Switch_ShouldTurnANewEntryOffWithoutSettingItUp_AndOnAgainBySettingItUp()
    {
        var catalog = Catalog();
        await using var host = await StartAsync(catalog);
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        catalog.SetUp = _ => Result<CatalogSetUp>.Ok(new CatalogSetUp([source]));
        var broker = scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>();

        var off = EngineHost.Data(await broker.SwitchAsync(Caex, false, "operator-key"));

        off.Should().Match<CatalogSourceModel>(entry => entry.State == CatalogState.Off && entry.ChangedBy == "operator-key" && entry.Sources.Count == 0);
        catalog.SetUps.Should().BeEmpty("a new entry turned off gets an off record and nothing is set up");

        var on = EngineHost.Data(await broker.SwitchAsync(Caex, true, "subject-7"));

        catalog.SetUps.Should().Equal(new[] { Caex });
        on.State.Should().Be(CatalogState.On);
        on.ChangedBy.Should().Be("subject-7");
        on.Sources.Should().ContainSingle().Which.SourceId.Should().Be(source);
        (await host.ScalarAsync<string>("SELECT catalog_key FROM harvest_sources WHERE id=@id", ("id", source))).Should().Be(Caex);
    }

    [Fact]
    public async Task Switch_ShouldSetANewEntryUpWhenTurnedOn_AndKeepItsOwnRecord()
    {
        var catalog = Catalog();
        await using var host = await StartAsync(catalog);
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        catalog.SetUp = _ => Result<CatalogSetUp>.Ok(new CatalogSetUp([source]));
        var broker = scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>();

        var first = EngineHost.Data(await broker.SwitchAsync(Afisha, true, "subject-1"));
        var again = EngineHost.Data(await broker.SwitchAsync(Afisha, true, "subject-2"));

        catalog.SetUps.Should().Equal(new[] { Afisha });
        first.State.Should().Be(CatalogState.On);
        again.Should().BeEquivalentTo(first, "turning on an entry that is on changes nothing, not even who changed it");
    }

    [Fact]
    public async Task Switch_ShouldAnswerConflictAndKeepTheRecord_WhenTheSetUpFails_AndNotFoundForAnUnknownKey()
    {
        var catalog = Catalog();
        catalog.SetUp = _ => Result<CatalogSetUp>.Fail(AppErrorFactory.Validation("Select a publisher."));
        await using var host = await StartAsync(catalog);
        using var scope = host.Services.CreateScope();
        var broker = scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>();

        var failed = await broker.SwitchAsync(Afisha, true, "subject-1");
        var unknown = await broker.SwitchAsync("test.nothing", true, "subject-1");

        var failure = failed.Should().BeOfType<Result<CatalogSourceModel>.Failure>().Subject.Error;
        failure.Type.Should().Be(AppErrorType.Conflict);
        failure.Message.Should().Be("Select a publisher.");
        unknown.Should().BeOfType<Result<CatalogSourceModel>.Failure>().Subject.Error.Type.Should().Be(AppErrorType.NotFound);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_catalog_sources")).Should().Be(0, "a failed set-up leaves the record as it was");
        (await broker.ListAsync()).Should().OnlyContain(entry => entry.State == CatalogState.New);
    }

    [Fact]
    public async Task Switch_ShouldSetAnOffEntryUpOnlyWhenNothingWasEverSetUp()
    {
        var catalog = Catalog();
        await using var host = await StartAsync(catalog);
        var registration = await host.RegisterAsync();
        using var scope = host.Services.CreateScope();
        var source = await SourceOfAsync(scope, registration.PipelineId);
        await host.ExecuteAsync("UPDATE harvest_sources SET catalog_key=@key WHERE id=@id", ("key", Anons), ("id", source));
        await CatalogSql.SetAsync(host.ConnectionString, Anons, false);
        await CatalogSql.SetAsync(host.ConnectionString, Caex, false);
        catalog.SetUp = _ => Result<CatalogSetUp>.Ok(new CatalogSetUp([]));
        var broker = scope.ServiceProvider.GetRequiredService<SourceCatalogBroker>();

        EngineHost.Data(await broker.SwitchAsync(Anons, true, "subject-1")).State.Should().Be(CatalogState.On);
        catalog.SetUps.Should().BeEmpty("an off entry that has sources resumes as it was");
        EngineHost.Data(await broker.SwitchAsync(Caex, true, "subject-1")).State.Should().Be(CatalogState.On);
        catalog.SetUps.Should().Equal(new[] { Caex }, "an entry turned off while new has nothing to resume, so turning it on sets it up");
    }

    [Fact]
    public async Task Reconcile_ShouldStampExistingSourcesAndRecordThemOn_WithoutChangingARecord_AndBeIdempotent()
    {
        var catalog = Catalog();
        await using var host = await StartAsync(catalog);
        var first = await host.RegisterAsync();
        var second = await host.RegisterAsync(host: "anons.uz");
        var third = await host.RegisterAsync(host: "caex.uz");
        var byHand = await host.RegisterAsync(host: "by-hand.example");
        using var scope = host.Services.CreateScope();
        var sources = new[]
        {
            await SourceOfAsync(scope, first.PipelineId), await SourceOfAsync(scope, second.PipelineId), await SourceOfAsync(scope, third.PipelineId),
        };
        catalog.Links = [new(Afisha, sources[0]), new(Anons, sources[1]), new(Caex, sources[2]), new(Caex, sources[2])];
        await CatalogSql.SetAsync(host.ConnectionString, Anons, false);

        await host.Services.ReconcileHarvestSourceCatalogAsync(CancellationToken.None);

        (await host.ScalarAsync<string>("SELECT string_agg(handle || '=' || coalesce(catalog_key,'-'), ',' ORDER BY handle) FROM harvest_sources"))
            .Should().Be("anons.uz=test.anons,by-hand.example=-,caex.uz=test.caex,mstar.uz=test.afisha");
        (await host.ScalarAsync<string>("SELECT string_agg(key || ':' || turned_on || ':' || changed_by, ',' ORDER BY key) FROM harvest_catalog_sources"))
            .Should().Be("test.afisha:true:existing,test.anons:false:test,test.caex:true:existing", "an existing record, the off one included, never changes");
        var recorded = await host.ScalarAsync<string>("SELECT string_agg(key || changed_at::text, ',' ORDER BY key) FROM harvest_catalog_sources");

        await host.Services.ReconcileHarvestSourceCatalogAsync(CancellationToken.None);

        (await host.ScalarAsync<string>("SELECT string_agg(key || changed_at::text, ',' ORDER BY key) FROM harvest_catalog_sources")).Should().Be(recorded);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_sources WHERE catalog_key IS NOT NULL")).Should().Be(3);
        using var again = host.Services.CreateScope();
        var list = await again.ServiceProvider.GetRequiredService<SourceCatalogBroker>().ListAsync();
        list.Select(entry => (entry.Key, entry.State)).Should().Equal((Afisha, CatalogState.On), (Anons, CatalogState.Off), (Caex, CatalogState.On));
        byHand.PipelineId.Should().NotBeEmpty();
    }

    private static TestCatalog Catalog() => new("test", TestCatalog.Entry(Afisha, "Afisha"), TestCatalog.Entry(Anons, "ANONS"), TestCatalog.Entry(Caex, "CAEx"));

    private static Task<EngineHost> StartAsync(ISourceCatalog catalog) => EngineHost.StartAsync(services =>
    {
        services.AddHarvestSources();
        services.AddSingleton(catalog);
    });

    private static async Task<Guid> SourceOfAsync(IServiceScope scope, Guid pipelineId) =>
        EngineHost.Data(await scope.ServiceProvider.GetRequiredService<IRegistrationBroker>().GetPipelineAsync(pipelineId)).SourceId;
}
