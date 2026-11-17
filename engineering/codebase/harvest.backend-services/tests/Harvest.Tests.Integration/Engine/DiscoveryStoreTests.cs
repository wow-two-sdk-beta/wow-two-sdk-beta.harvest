using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;

namespace Harvest.Tests.Integration.Engine;

/// <summary>Exercises durable discovery identity, detail state and section membership through staged runs.</summary>
public sealed class DiscoveryStoreTests
{
    private const string Host = "discovery.example.test";
    private const string Catalog = "https://" + Host + "/sitemap.xml";
    private const string Detail = "https://" + Host + "/events/1";

    [Fact]
    public async Task DiscoveryStore_ShouldAdvanceRevisionOnAChangedObservationOfTheSameExternalKey()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(Host);
        PublishCatalog(host, "2026-10-25T09:00:00Z");
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        var id = await host.ScalarAsync<Guid>("SELECT id FROM harvest_discoveries");
        var externalKey = await host.ScalarAsync<string>("SELECT external_key FROM harvest_discoveries");
        (await host.ScalarAsync<long>("SELECT revision FROM harvest_discoveries WHERE id=@id", ("id", id))).Should().Be(1);

        PublishCatalog(host, "2026-10-26T09:00:00Z");
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries")).Should().Be(1);
        (await host.ScalarAsync<string>("SELECT external_key FROM harvest_discoveries WHERE id=@id", ("id", id)))
            .Should().Be(externalKey);
        (await host.ScalarAsync<long>("SELECT revision FROM harvest_discoveries WHERE id=@id", ("id", id))).Should().Be(2);
    }

    [Fact]
    public async Task DiscoveryStore_ShouldMarkTheDiscoveryGoneWhenTheDetailIsGone()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(Host);
        PublishCatalog(host, "2026-10-25T09:00:00Z");
        await RunAsync(host, registration.PipelineId, WorkKind.Discover);
        var id = await host.ScalarAsync<Guid>("SELECT id FROM harvest_discoveries");
        (await host.ScalarAsync<string>("SELECT detail_state FROM harvest_discoveries WHERE id=@id", ("id", id)))
            .Should().Be("pending");
        host.Fetcher.Set(Detail, string.Empty, status: 410);

        await RunAsync(host, registration.PipelineId, WorkKind.Harvest);

        (await host.ScalarAsync<string>("SELECT detail_state FROM harvest_discoveries WHERE id=@id", ("id", id)))
            .Should().Be("gone");
        host.Fetcher.Calls.Should().Equal(Catalog, Detail);
    }

    [Fact]
    public async Task DiscoveryStore_ShouldShareOneDiscoveryAcrossTwoPipelineScopes()
    {
        await using var host = await EngineHost.StartAsync();
        var registration = await host.RegisterAsync(Host);
        using var scope = host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var wholeSource = EngineHost.Data(await registry.GetPipelineAsync(registration.PipelineId));
        var section = EngineHost.Data(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = wholeSource.ContainerId,
            SourceId = wholeSource.SourceId,
            Scope = new() { Url = Catalog },
        }));
        section.Id.Should().NotBe(wholeSource.Id);
        PublishCatalog(host, "2026-10-25T09:00:00Z");

        await RunAsync(host, wholeSource.Id, WorkKind.Discover);
        await RunAsync(host, section.Id, WorkKind.Discover);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_discoveries")).Should().Be(1);
        var memberships = await host.ScalarAsync<Guid[]>("""
            SELECT array_agg(scope.pipeline_id)
            FROM harvest_discovery_scopes scope
            JOIN harvest_discoveries discovery ON discovery.id=scope.discovery_id
            WHERE discovery.url=@detail AND scope.catalog_url=@catalog
            """, ("detail", Detail), ("catalog", Catalog));
        memberships.Should().BeEquivalentTo(new[] { wholeSource.Id, section.Id });
    }

    private static void PublishCatalog(EngineHost host, string lastModified) => host.Fetcher.Set(Catalog,
        $"<urlset><url><loc>{Detail}</loc><lastmod>{lastModified}</lastmod></url></urlset>", "application/xml");

    private static async Task RunAsync(EngineHost host, Guid pipelineId, WorkKind kind)
    {
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        using var scope = host.Services.CreateScope();
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        EngineHost.Data(await runs.StartAsync(pipelineId, kind));
        (await host.DrainAsync()).Should().Be(1);
    }
}
