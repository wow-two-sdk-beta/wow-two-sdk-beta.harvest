using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Unit.Catalog;

/// <summary>The catalogs the image ships to an instance: the Ocharo registry's collectible publishers and the marketplace sites,
/// and how setup treats an entry the instance turned off.</summary>
public sealed class SourceCatalogTests
{
    [Fact]
    public void OcharoCatalog_ShouldListTheCollectibleGroupsOnly_UnderOcharoKeys()
    {
        var registry = OcharoSourceRegistry.Current;
        var collectible = registry.Groups.Where(group => group.Channels.Any(channel => channel.Mode is "harvest" or "provider")).ToArray();

        var entries = Ocharo().Entries();

        entries.Should().HaveCount(27).And.OnlyContain(entry => entry.Key.StartsWith("ocharo.", StringComparison.Ordinal)
            && entry.Catalog == "ocharo" && entry.Kind == CatalogKind.Events && entry.Channels >= 1 && entry.Hosts.Count >= 1);
        entries.Select(entry => entry.Key).Should().Equal(collectible.OrderBy(group => group.Rank).Select(group => "ocharo." + group.Id));
        var planned = registry.Groups.Except(collectible).Select(group => "ocharo." + group.Id);
        planned.Should().NotBeEmpty("the registry holds groups nothing of which could run").And.NotIntersectWith(entries.Select(entry => entry.Key));
        var afisha = entries.Single(entry => entry.Key == "ocharo.afisha");
        afisha.Name.Should().Be("Afisha Uzbekistan");
        afisha.Channels.Should().Be(2);
        afisha.Hosts.Should().Equal([new CatalogHost(SourceFamily.Site, "www.afisha.uz")], "two channels, one host, one source");
        entries.Where(entry => entry.Hosts.Any(host => host.Family == SourceFamily.Instagram)).Should().NotBeEmpty("an Instagram publisher is an entry too");
    }

    [Fact]
    public void MarketplaceCatalog_ShouldListEachDistinctSiteOnce_UnderMarketplaceKeys()
    {
        var entries = Marketplace().Entries();

        entries.Select(entry => entry.Key).Should().Equal("marketplace.olx-uz", "marketplace.joymee-uz", "marketplace.birbir-uz", "marketplace.domtut-uz");
        entries.Should().OnlyContain(entry => entry.Catalog == "marketplace" && entry.Kind == CatalogKind.Listings);
        entries.Select(entry => entry.Channels).Should().Equal(3, 1, 2, 1);
        entries.Single(entry => entry.Key == "marketplace.olx-uz").Hosts.Should().Equal([new CatalogHost(SourceFamily.Site, "www.olx.uz")]);
        entries.Single(entry => entry.Key == "marketplace.birbir-uz").Hosts.Should().BeEquivalentTo(
        [
            new CatalogHost(SourceFamily.Manual, BirBirSourceAdapter.Host), new CatalogHost(SourceFamily.Site, BirBirSourceAdapter.Host),
        ], "BirBir's import and collection variants are two sources of one entry");
    }

    [Fact]
    public async Task OcharoSetup_ShouldTurnOnEveryEntryItSetsUp_SkipAnOffEntryWhenItNamesNoGroup_AndTurnItOnWhenItNamesIt()
    {
        var records = new FakeRecords { Off = { "ocharo.afisha" } };
        var provisioner = Provisioner(records);

        var bulk = Value(await provisioner.EnsureAsync());

        bulk.Skipped.Should().Equal("ocharo.afisha");
        bulk.Sources.Should().NotContain(row => row.GroupId == "afisha").And.Contain(row => row.GroupId == "anons");
        records.SetUps.Select(call => call.Key).Should().Contain("ocharo.anons").And.NotContain("ocharo.afisha");
        records.SetUps.Should().OnlyContain(call => !call.OverrideOff && call.Sources.Count >= 1);
        records.SetUps.Select(call => call.Key).Should().OnlyHaveUniqueItems();

        records.SetUps.Clear();
        var named = Value(await provisioner.EnsureAsync(groupIds: ["afisha"]));

        named.Skipped.Should().BeEmpty();
        named.Sources.Should().OnlyContain(row => row.GroupId == "afisha");
        var call = records.SetUps.Should().ContainSingle().Subject;
        (call.Key, call.OverrideOff, call.Sources.Count).Should().Be(("ocharo.afisha", true, 1), "naming the group turns it on even when it was off; its two channels share one source");
    }

    [Fact]
    public async Task OcharoSetup_ShouldLeaveTheStampingToItsCaller_WhenAskedNotToRecord()
    {
        var records = new FakeRecords { Off = { "ocharo.afisha" } };

        var setup = Value(await Provisioner(records).EnsureAsync(groupIds: ["afisha"], recordCatalog: false));

        records.SetUps.Should().BeEmpty("the catalog's own set-up stamps and records after every step succeeded");
        setup.Sources.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MarketplaceSetup_ShouldSkipAnOffSiteForAProfile_AndSetUpOnlyTheNamedSiteForAnEntry()
    {
        var records = new FakeRecords { Off = { "marketplace.olx-uz" } };
        var provisioner = MarketplaceProvisioner(records);

        var bulk = Value(await provisioner.EnsureAsync(MarketplaceProfiles.Haven));

        bulk.Skipped.Should().Equal("marketplace.olx-uz");
        bulk.Searches.Select(search => search.Key).Should().Equal("joymee-apartments-rent", "birbir-apartments-rent", "birbir-collection", "domtut-apartments-rent");
        records.SetUps.Select(call => call.Key).Should().BeEquivalentTo(["marketplace.joymee-uz", "marketplace.birbir-uz", "marketplace.domtut-uz"]);
        records.SetUps.Single(call => call.Key == "marketplace.birbir-uz").Sources.Should().HaveCount(2);
        records.SetUps.Should().OnlyContain(call => !call.OverrideOff);

        records.SetUps.Clear();
        var site = Value(await provisioner.EnsureAsync(MarketplaceProfiles.Haven, siteKey: "olx-uz"));

        site.Skipped.Should().BeEmpty();
        site.Searches.Select(search => search.Key).Should().Equal("apartments-rent", "houses-rent", "daily-rent");
        var call = records.SetUps.Should().ContainSingle().Subject;
        (call.Key, call.OverrideOff, call.Sources.Count).Should().Be(("marketplace.olx-uz", true, 1));
    }

    [Fact]
    public async Task Setup_ShouldSkipNothingAndStampNothing_WithoutTheInstancesCatalogRecords()
    {
        var registry = new FakeRegistrationBroker();
        var delivery = new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]);

        var setup = Value(await new OcharoSourceProvisioner(registry, delivery).EnsureAsync());

        setup.Skipped.Should().BeEmpty();
        setup.Sources.Should().HaveCount(30);
    }

    private static OcharoSourceCatalog Ocharo() => new(new FakeRegistrationBroker(), Provisioner(new FakeRecords()));

    private static MarketplaceSourceCatalog Marketplace() => new(new FakeRegistrationBroker(), MarketplaceProvisioner(new FakeRecords()));

    private static OcharoSourceProvisioner Provisioner(FakeRecords records)
    {
        var registry = new FakeRegistrationBroker();
        return new OcharoSourceProvisioner(registry, new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new OcharoFashionFilter()]), records);
    }

    private static MarketplaceProvisioner MarketplaceProvisioner(FakeRecords records)
    {
        var registry = new FakeRegistrationBroker();
        return new MarketplaceProvisioner(registry, new FakeDeliveryBroker(new FakePostBroker(registry), registry, null, [new MarketplaceListingFilter()]), records);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class FakeRecords : ISourceCatalogRecords
    {
        internal HashSet<string> Off { get; } = [];

        internal List<(string Key, IReadOnlyCollection<Guid> Sources, bool OverrideOff)> SetUps { get; } = [];

        public Task<IReadOnlySet<string>> OffKeysAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(Off);

        public Task RecordSetUpAsync(string entryKey, IReadOnlyCollection<Guid> sourceIds, bool overrideOff, CancellationToken cancellationToken = default)
        {
            SetUps.Add((entryKey, sourceIds, overrideOff));
            return Task.CompletedTask;
        }
    }
}
