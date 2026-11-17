using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>One normalized rental listing submitted by an operator from an authorized source.</summary>
public sealed record MarketplaceListingImport(
    string ExternalId, string Url, string Title, string? Description = null, string? PriceText = null,
    string? City = null, string? District = null, string? SellerName = null, DateTimeOffset? PublishedAt = null,
    IReadOnlyDictionary<string, string>? Attributes = null, IReadOnlyList<string>? ImageUrls = null);

/// <summary>Saves a submitted listing in a provisioned marketplace subscription without fetching its URL.</summary>
public sealed class MarketplaceListingImporter(
    MarketplaceProvisioner provisioner, IRegistrationBroker registry, IPostBroker posts, TimeProvider time)
{
    /// <summary>Validates and stores a rental under its original marketplace identity.</summary>
    /// <param name="profile">The tenant profile that owns the import subscription.</param>
    /// <param name="searchKey">The key of the profile's import-only search.</param>
    /// <param name="item">The listing supplied by the operator.</param>
    /// <param name="cancellationToken">The token that cancels the import.</param>
    /// <returns>The capture result, or the reason it was rejected.</returns>
    public async Task<Result<CaptureSaveResult>> ImportAsync(MarketplaceProfile profile, string searchKey,
        MarketplaceListingImport item, CancellationToken cancellationToken)
    {
        var search = profile.Searches.SingleOrDefault(search => search.Key == searchKey && search.Site.ImportOnly);
        if (search is null)
            return Result<CaptureSaveResult>.Fail(AppErrorFactory.NotFound("No marketplace import matches this search."));
        if (!Uri.TryCreate(item.Url, UriKind.Absolute, out var url) || !IsHttps(url)
            || url.IdnHost != search.Site.Handle && url.IdnHost != "www." + search.Site.Handle)
            return Invalid("Use an HTTPS listing URL on the selected marketplace.");

        var externalId = item.ExternalId?.Trim();
        var title = item.Title?.Trim();
        if (externalId is not { Length: > 0 and <= 128 } || title is not { Length: > 0 and <= 500 })
            return Invalid("A marketplace listing requires its original ID and a title.");
        if (item.Description?.Length > 24000 || item.PriceText?.Length > 128 || item.City?.Length > 256
            || item.District?.Length > 256 || item.SellerName?.Length > 256)
            return Invalid("A marketplace listing exceeds the allowed field lengths.");
        if (search.Site == MarketplaceSite.BirBir && !BirBirSourceFields.MatchesListing(url, externalId))
            return Invalid("Use the BirBir Tashkent apartment rental URL and its matching ad ID.");
        if (search.Site == MarketplaceSite.Domtut && (url.AbsolutePath == "/"
            || url.AbsolutePath.TrimEnd('/').Equals("/filter", StringComparison.OrdinalIgnoreCase)
            || url.AbsolutePath.StartsWith("/filter/", StringComparison.OrdinalIgnoreCase)))
            return Invalid("Use the original Domtut listing URL.");

        var images = item.ImageUrls ?? [];
        if (images.Count > 30 || images.Any(image => !IsListingImage(search.Site, image)))
            return Invalid("Use up to 30 HTTPS image URLs hosted by the marketplace.");
        var attributes = item.Attributes ?? new Dictionary<string, string>();
        if (attributes.Count > 40 || attributes.Any(pair => pair.Key.Length > 100 || pair.Value is null || pair.Value.Length > 500))
            return Invalid("A marketplace listing exceeds the allowed attribute count or length.");

        var status = await provisioner.ReadAsync(profile, cancellationToken);
        if (status is Result<MarketplaceStatus>.Failure failure)
            return Result<CaptureSaveResult>.Fail(failure.Error);
        var registration = ((Result<MarketplaceStatus>.Success)status).Value;
        var target = registration.Searches.Single(row => row.Key == searchKey);
        if (target.Pipeline is not { } pipeline || target.SubscriptionId is null)
            return Result<CaptureSaveResult>.Fail(AppErrorFactory.NotFound("Run marketplace setup before importing listings."));
        if (await registry.GetSourceAsync(pipeline.SourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var registered }
            || registered.State != SourceState.Active || registered.Permission != SourcePermission.Restricted)
            return Result<CaptureSaveResult>.Fail(AppErrorFactory.Conflict("The marketplace source is unavailable for imports."));

        string canonical = new UriBuilder(url) { Fragment = string.Empty, Query = string.Empty }.Uri.AbsoluteUri;
        var published = item.PublishedAt?.ToUniversalTime();
        var postedAtText = published?.ToString("O");
        var imageUrls = images.Distinct(StringComparer.Ordinal).ToArray();
        var source = search.Site == MarketplaceSite.BirBir
            ? BirBirSourceFields.FromImport(externalId, canonical, title, item.Description, item.PriceText, item.City,
                item.District, item.SellerName, published, attributes, imageUrls).ToEnvelope("marketplace-import@1")
            : JsonSerializer.SerializeToElement(new
        {
            schema = "domtut.rental.v1",
            parserVersion = "marketplace-import@1",
            fields = new
            {
                title, item.Description, item.PriceText, item.City, item.District, item.SellerName, postedAtText,
                attributes = attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                    .ToDictionary(attribute => attribute.Key, attribute => attribute.Value, StringComparer.Ordinal),
                imageUrls,
            },
        }, JsonSerializerOptions.Web);
        var capture = new Capture
        {
            ExternalKey = externalId, Url = canonical, RequestUrl = canonical, FinalUrl = canonical,
            OriginKey = $"{search.Site.Handle}:ad:{externalId}", CapturedAtUtc = time.GetUtcNow(), PublishedAtUtc = published,
            Body = ReadOnlyMemory<byte>.Empty,
            ContentProjection = JsonSerializer.Serialize(new { canonical, source }),
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Listing, Title = title, Text = item.Description,
            Author = item.SellerName is null ? null : new() { Name = item.SellerName },
            PostedAt = postedAtText is null ? null : new() { Value = postedAtText, Precision = DatePrecision.Second },
            Media = imageUrls.Select(image => new PostMedia { Kind = MediaKind.Image, Url = image }).ToArray(),
            Prices = item.PriceText is null ? [] : [new() { Raw = item.PriceText }],
            Extras = new Dictionary<string, JsonElement> { ["source"] = source },
            Extraction = new() { NormVersion = "marketplace-import@1", RulesVersion = "marketplace-import@1" },
        };
        return await posts.SaveCaptureAsync(registration.ContainerId, pipeline.SourceId, null, capture, envelope, cancellationToken);
    }

    private static bool IsHttps(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0;

    private static bool IsListingImage(MarketplaceSite site, string? image) => image is { Length: > 0 and <= 2048 }
        && Uri.TryCreate(image, UriKind.Absolute, out var address) && IsHttps(address)
        && (address.IdnHost == site.Handle || address.IdnHost.EndsWith("." + site.Handle, StringComparison.Ordinal));

    private static Result<CaptureSaveResult> Invalid(string message) => Result<CaptureSaveResult>.Fail(AppErrorFactory.Validation(message));
}
