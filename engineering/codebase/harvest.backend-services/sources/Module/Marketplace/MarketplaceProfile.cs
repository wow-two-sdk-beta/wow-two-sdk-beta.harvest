using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>One consuming product's marketplace intake: the tenant and private container it owns, the saved searches that
/// feed it, their cadence and run budget, and the name of the key the product pulls with.</summary>
/// <remarks>A profile is data. <see cref="MarketplaceProvisioner"/> applies any profile the same way, so a new product
/// adds a profile, not a provisioner.</remarks>
public sealed record MarketplaceProfile
{
    /// <summary>Gets the profile's stable key, as the verb and route name it.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the slug of the tenant that owns the container and subscriptions.</summary>
    public required string TenantSlug { get; init; }

    /// <summary>Gets the tenant's display name, used only when setup creates the tenant.</summary>
    public required string TenantName { get; init; }

    /// <summary>Gets the external reference that identifies the profile's private container inside the tenant.</summary>
    public required string ContainerRef { get; init; }

    /// <summary>Gets the name of the consumer's tenant API key, such as the program that holds it.</summary>
    public required string Consumer { get; init; }

    /// <summary>Gets the <c>catalog</c> value echoed in each subscription's context.</summary>
    public required string Catalog { get; init; }

    /// <summary>Gets the saved marketplace searches, one pipeline and one subscription each.</summary>
    public required IReadOnlyList<MarketplaceSearch> Searches { get; init; }

    /// <summary>Gets how often an enabled capture schedule reads each search.</summary>
    public TimeSpan CaptureEvery { get; init; } = TimeSpan.FromHours(2);

    /// <summary>Gets how often an enabled check schedule re-checks known ads.</summary>
    public TimeSpan CheckEvery { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Gets the time zone schedules are declared in.</summary>
    public string TimeZone { get; init; } = "Asia/Tashkent";

    /// <summary>Gets the budget of one run of a search's pipeline.</summary>
    /// <remarks>A run's page count includes every ad detail it reads, so <c>MaxPages</c> is the items plus a few search
    /// pages; <c>MaxItems</c> is what bounds a run. An OLX search page weighs about 2 MB.</remarks>
    public PipelineLimits Limits { get; init; } = new()
    {
        MaxPages = 64, MaxItems = 60, MaxRequests = 70, MaxBytes = 160_000_000, MaxDuration = TimeSpan.FromMinutes(20), MaxProbes = 100,
    };
}

/// <summary>One saved marketplace search: a category or search URL with every query filter it needs.</summary>
/// <param name="Key">The search's stable key inside its profile, echoed as the subscription's <c>search</c> context.</param>
/// <param name="Name">The pipeline's display name.</param>
/// <param name="Url">The complete public search URL the pipeline starts from.</param>
public sealed record MarketplaceSearch(string Key, string Name, string Url);
