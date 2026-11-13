using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace HarvestAdapters.Tests.Unit.Marketplace;

public sealed class MarketplaceProfilesTests
{
    [Fact]
    public void Profiles_ShouldHaveDistinctKeysTenantsAndSearches()
    {
        MarketplaceProfiles.All.Select(profile => profile.Key).Should().OnlyHaveUniqueItems();
        MarketplaceProfiles.All.Select(profile => profile.TenantSlug).Should().OnlyHaveUniqueItems();
        foreach (MarketplaceProfile profile in MarketplaceProfiles.All)
        {
            profile.Searches.Should().NotBeEmpty();
            profile.Searches.Select(search => search.Key).Should().OnlyHaveUniqueItems();
            profile.Searches.Select(search => search.Url).Should().OnlyHaveUniqueItems();
            profile.Limits.MaxPages.Should().BeInRange(profile.Limits.MaxItems!.Value + 1, profile.Limits.MaxItems.Value + 5,
                "a run's pages count each ad it reads, plus only the newest search pages");
            profile.CaptureEvery.Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(1));
        }
    }

    [Fact]
    public void Searches_ShouldBePublicOlxCatalogUrlsReadNewestFirst()
    {
        var reader = new OlxCaptureReader();
        foreach (MarketplaceSearch search in MarketplaceProfiles.All.SelectMany(profile => profile.Searches))
        {
            var url = new Uri(search.Url);
            url.IdnHost.Should().Be(search.Site.Handle, search.Key);
            if (search.Site != MarketplaceSite.Olx)
                continue;
            reader.Allows(url).Should().BeTrue(search.Key);
            Uri.UnescapeDataString(url.Query).Should().Be("?search[order]=created_at:desc", search.Key);
        }
    }

    [Fact]
    public void Haven_ShouldReadOnlyTashkentResidentialRent()
    {
        MarketplaceProfile haven = MarketplaceProfiles.Find("haven")!;

        haven.TenantSlug.Should().Be("haven");
        haven.Consumer.Should().Be("haven-supply");
        haven.Searches.Should().OnlyContain(search => search.Url.StartsWith("https://www.olx.uz/nedvizhimost/", StringComparison.Ordinal)
            && search.Url.Contains("/tashkent/", StringComparison.Ordinal) && !search.Url.Contains("prodazha", StringComparison.Ordinal));
        MarketplaceProfiles.Find("unknown").Should().BeNull();
    }

    [Theory]
    [InlineData(PostKind.Listing, true)]
    [InlineData(PostKind.Event, false)]
    [InlineData(PostKind.Other, false)]
    public void ListingFilter_ShouldPassListingsOnly(PostKind kind, bool expected)
    {
        var envelope = new PostEnvelope
        {
            Kind = kind, Title = "2-room flat", Extraction = new() { NormVersion = "test@1", RulesVersion = "test@1" },
        };

        new MarketplaceListingFilter().Matches(envelope).Should().Be(expected);
    }
}
