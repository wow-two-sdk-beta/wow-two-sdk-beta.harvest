namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Holds the names the Layer 2 extraction shares with hosts, prompts and the entity layer.</summary>
public static class PostExtractionConstants
{
    /// <summary>The versioned AI use case that parses an event post; also its post work kind.</summary>
    public const string EventUseCase = "extract-event";

    /// <summary>The versioned AI use case that parses a listing post; also its post work kind.</summary>
    public const string ListingUseCase = "extract-listing";

    /// <summary>The entity type <see cref="EventUseCase"/> serves, as <c>post-tags</c> names it.</summary>
    public const string EventType = "event";

    /// <summary>The entity type <see cref="ListingUseCase"/> serves, as <c>post-tags</c> names it.</summary>
    public const string ListingType = "listing";

    /// <summary>The host configuration section of <see cref="PostExtractionOptions"/>.</summary>
    public const string SectionName = "Harvest:Processing:Extraction";

    /// <summary>The model route used when the host names none.</summary>
    public const string DefaultModel = "anthropic/claude-sonnet-5-5";

    /// <summary>The taxonomy key whose primary domain fills the event answer's <c>fashion</c> block.</summary>
    public const string FashionDomain = "style-fashion";

    /// <summary>The taxonomy key whose primary domain, on a rent offer, fills the listing answer's <c>rentalTerms</c>.</summary>
    public const string RealEstateDomain = "real-estate";

    /// <summary>Gets both use cases, event first.</summary>
    public static IReadOnlyList<string> UseCases { get; } = [EventUseCase, ListingUseCase];

    /// <summary>Returns the use case that serves an entity type, or null for <c>none</c>, <c>unsure</c> or anything else.</summary>
    /// <param name="entityType">The entity type, as <c>post-tags</c> names it.</param>
    public static string? UseCaseFor(string? entityType) => entityType switch
    {
        EventType => EventUseCase,
        ListingType => ListingUseCase,
        _ => null,
    };
}
