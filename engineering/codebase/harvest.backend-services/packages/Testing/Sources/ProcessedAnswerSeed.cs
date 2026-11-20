using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Sources;

/// <summary>Describes the AI answers a test writes straight into an implementation's store for one post, as the
/// <c>post-tags</c> pass and extraction would have left them about the post's current title and text.</summary>
public sealed record ProcessedAnswerSeed
{
    /// <summary>Gets the post, which the store already holds.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the tags answer's status: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public string Status { get; init; } = "done";

    /// <summary>Gets why a failed or unavailable answer has no tags.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the entity type: <c>event</c>, <c>listing</c>, <c>none</c> or <c>unsure</c>; none for an answer
    /// that is not done.</summary>
    public string? EntityType { get; init; } = "event";

    /// <summary>Gets the taxonomy keys the answer kept, the primary one first.</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>Gets the open English topics.</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Gets what the post is about.</summary>
    public string? About { get; init; }

    /// <summary>Gets the timeframe; <c>upcoming</c> unless stated.</summary>
    public string? Timeframe { get; init; } = "upcoming";

    /// <summary>Gets the English title.</summary>
    public string? TitleEn { get; init; }

    /// <summary>Gets the confidence, 0 to 1.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>Gets the flags.</summary>
    public IReadOnlyList<ProcessedFlagModel> Flags { get; init; } = [];

    /// <summary>Gets the route that answered, <c>{provider}/{model}</c>.</summary>
    public string? Model { get; init; } = "anthropic/claude-haiku-5-5";

    /// <summary>Gets the prompt version that answered.</summary>
    public int? PromptVersion { get; init; } = 1;

    /// <summary>Gets when the answer was stored.</summary>
    public required DateTimeOffset AnsweredAtUtc { get; init; }

    /// <summary>Gets the extraction answer about the same content, when one ran.</summary>
    public ProcessedExtractionSeed? Extraction { get; init; }
}

/// <summary>Describes an extraction answer a test seeds beside a post's tags.</summary>
public sealed record ProcessedExtractionSeed
{
    /// <summary>Gets the use case: <c>extract-event</c> or <c>extract-listing</c>.</summary>
    public string UseCase { get; init; } = "extract-event";

    /// <summary>Gets the status: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public string Status { get; init; } = "done";

    /// <summary>Gets the role.</summary>
    public string? Role { get; init; } = "announcement";

    /// <summary>Gets the event an <c>extract-event</c> answer states; a store writes it in the answer's own shape.</summary>
    public ProcessedEventSummary? Event { get; init; }

    /// <summary>Gets the listing an <c>extract-listing</c> answer states.</summary>
    public ProcessedListingSummary? Listing { get; init; }

    /// <summary>Gets the quotes the answer keeps behind its fields, as the details read returns them; a store writes each into
    /// the answer's own shape: <c>occurrences[i]</c> and <c>prices[i]</c> as an item's <c>evidence</c>, <c>price</c> as the
    /// listing price's, <c>registration.opens</c> and <c>.deadline</c> as an evidenced date, <c>rentalTerms.{term}</c> as a
    /// term with a claim per quote.</summary>
    public IReadOnlyList<ProcessedEvidenceModel> Evidence { get; init; } = [];

    /// <summary>Gets the flags.</summary>
    public IReadOnlyList<ProcessedFlagModel> Flags { get; init; } = [];

    /// <summary>Gets the route that answered.</summary>
    public string? Model { get; init; } = "anthropic/claude-haiku-5-5";

    /// <summary>Gets the prompt version that answered.</summary>
    public int? PromptVersion { get; init; } = 2;
}
