using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents one post a source shows with what the AI passes made of it: its tags, its relevance verdict and,
/// for an extracted event or listing, a compact extraction summary. It carries no post text beyond the title.</summary>
public sealed record SourceProcessedPostModel
{
    /// <summary>Gets the post's id.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the resolved title, else the source link's URL.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the URL of the source's first link to the post.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the post's revision.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets when the post was first captured (UTC).</summary>
    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>Gets the tags answer the post carries: the done answer about its current title and text, else the
    /// newest done one, else the newest attempt.</summary>
    public required ProcessedTagsModel Tags { get; init; }

    /// <summary>Gets the relevance verdict the post carries, as subscribers receive it; none without one.</summary>
    public PostClassification? Classification { get; init; }

    /// <summary>Gets the extraction answer about the same content as <see cref="Tags"/>, for an event or a listing;
    /// none when no extraction ran.</summary>
    public ProcessedExtractionModel? Extraction { get; init; }
}

/// <summary>Represents a post's <c>post-tags</c> answer: entity type, domains, topics and routing fields.</summary>
public sealed record ProcessedTagsModel
{
    /// <summary>Gets the answer's status: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why a failed or unavailable answer has no tags.</summary>
    public string? Error { get; init; }

    /// <summary>Gets whether the answer is about the post's current title and text.</summary>
    public bool Current { get; init; }

    /// <summary>Gets the entity type: <c>event</c>, <c>listing</c>, <c>none</c> or <c>unsure</c>.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the taxonomy nodes the answer kept, the primary one first.</summary>
    public IReadOnlyList<ProcessedDomainModel> Domains { get; init; } = [];

    /// <summary>Gets the open English topics.</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Gets what the post is about, in a few English words.</summary>
    public string? About { get; init; }

    /// <summary>Gets when the post's subject happens: <c>upcoming</c>, <c>ongoing</c>, <c>past</c>, <c>evergreen</c> or
    /// <c>unsure</c>.</summary>
    public string? Timeframe { get; init; }

    /// <summary>Gets the title in English.</summary>
    public string? TitleEn { get; init; }

    /// <summary>Gets the answer's confidence, 0 to 1.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>Gets the fields the model or the checks were unsure of.</summary>
    public IReadOnlyList<ProcessedFlagModel> Flags { get; init; } = [];

    /// <summary>Gets who answered, with what prompt, at what cost and how fast.</summary>
    public required ProcessedAnswerModel Answer { get; init; }
}

/// <summary>Represents one taxonomy node of a tags answer.</summary>
/// <param name="Key">The node's key, such as <c>style-fashion/womens-fashion</c>.</param>
/// <param name="Label">The node's English label; the key when the taxonomy does not hold it.</param>
/// <param name="Primary">Whether the node is the answer's primary domain.</param>
public sealed record ProcessedDomainModel(string Key, string Label, bool Primary);

/// <summary>Represents a field the model or the checks were unsure of.</summary>
/// <param name="Field">The field, as a JSON path.</param>
/// <param name="Reason">Why it was flagged.</param>
/// <param name="Proposed">The value the model proposed, when it gave one.</param>
public sealed record ProcessedFlagModel(string Field, string Reason, string? Proposed);

/// <summary>Represents an AI answer's provenance: the provider and model that answered, the prompt version, and the
/// cost and latency the inference log recorded for the attempt.</summary>
public sealed record ProcessedAnswerModel
{
    /// <summary>Gets the provider, such as <c>anthropic</c> or <c>openai</c>; none when no model answered.</summary>
    public string? Provider { get; init; }

    /// <summary>Gets the model id, such as <c>claude-haiku-5-5</c>.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the prompt version that answered.</summary>
    public int? PromptVersion { get; init; }

    /// <summary>Gets the attempt's cost in micro-USD; none when the host keeps no inference log or the log holds no
    /// row for the attempt.</summary>
    public long? CostMicroUsd { get; init; }

    /// <summary>Gets the attempt's wall-clock time in milliseconds; none as for <see cref="CostMicroUsd"/>.</summary>
    public long? LatencyMs { get; init; }

    /// <summary>Gets when the answer was stored (UTC).</summary>
    public required DateTimeOffset AnsweredAt { get; init; }
}

/// <summary>Represents a post's extraction answer: its use case, its role and a compact summary of what it extracted.</summary>
public sealed record ProcessedExtractionModel
{
    /// <summary>Gets the use case: <c>extract-event</c> or <c>extract-listing</c>.</summary>
    public required string UseCase { get; init; }

    /// <summary>Gets the answer's status: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why a failed or unavailable answer has no summary.</summary>
    public string? Error { get; init; }

    /// <summary>Gets what the post does for its entity: <c>announcement</c>, <c>update</c>, <c>report</c>,
    /// <c>mention</c> or <c>unsure</c>.</summary>
    public string? Role { get; init; }

    /// <summary>Gets the event summary of a done <c>extract-event</c> answer.</summary>
    public ProcessedEventSummary? Event { get; init; }

    /// <summary>Gets the listing summary of a done <c>extract-listing</c> answer.</summary>
    public ProcessedListingSummary? Listing { get; init; }

    /// <summary>Gets the fields the model or the checks were unsure of.</summary>
    public IReadOnlyList<ProcessedFlagModel> Flags { get; init; } = [];

    /// <summary>Gets who answered, with what prompt, at what cost and how fast.</summary>
    public required ProcessedAnswerModel Answer { get; init; }
}

/// <summary>Represents the parts of an <c>extract-event</c> answer a card shows.</summary>
public sealed record ProcessedEventSummary
{
    /// <summary>Gets the canonical English name, without year or ordinal.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the name as the source writes it.</summary>
    public string? OriginalName { get; init; }

    /// <summary>Gets the edition label as written, such as <c>2026</c> or <c>9th</c>.</summary>
    public string? EditionLabel { get; init; }

    /// <summary>Gets the runs the post names, each with its dates, precision and certainty.</summary>
    public IReadOnlyList<ProcessedOccurrenceModel> Occurrences { get; init; } = [];

    /// <summary>Gets the venue.</summary>
    public string? Venue { get; init; }

    /// <summary>Gets the city.</summary>
    public string? City { get; init; }

    /// <summary>Gets the country.</summary>
    public string? Country { get; init; }

    /// <summary>Gets the format, such as <c>runway</c> or <c>trade-fair</c>.</summary>
    public string? Format { get; init; }

    /// <summary>Gets how one gets in: <c>free</c>, <c>ticketed</c>, <c>application</c> or <c>unknown</c>.</summary>
    public string? Access { get; init; }

    /// <summary>Gets the stated prices.</summary>
    public IReadOnlyList<ProcessedPriceModel> Prices { get; init; } = [];

    /// <summary>Gets the event's state as the post tells it.</summary>
    public string? Status { get; init; }
}

/// <summary>Represents one run of an event as an extraction answer states it.</summary>
/// <param name="Starts">The first date, partial ISO 8601.</param>
/// <param name="Ends">The last date, when stated.</param>
/// <param name="Precision">The finest unit stated: <c>year</c>, <c>month</c>, <c>day</c>, <c>hour</c> or <c>minute</c>.</param>
/// <param name="Certainty">Whether the post <c>stated</c> the dates or the model <c>estimated</c> them.</param>
public sealed record ProcessedOccurrenceModel(string? Starts, string? Ends, string? Precision, string? Certainty);

/// <summary>Represents a stated price.</summary>
/// <param name="Amount">The amount; none for a price stated without one.</param>
/// <param name="Currency">The currency code.</param>
/// <param name="Label">What the price buys, or its period for a rent.</param>
public sealed record ProcessedPriceModel(decimal? Amount, string? Currency, string? Label);

/// <summary>Represents the parts of an <c>extract-listing</c> answer a card shows.</summary>
public sealed record ProcessedListingSummary
{
    /// <summary>Gets the offer type: <c>sale</c>, <c>rent</c>, <c>service</c> or <c>unsure</c>.</summary>
    public string? OfferType { get; init; }

    /// <summary>Gets the asking price, with its period for a rent.</summary>
    public ProcessedPriceModel? Price { get; init; }

    /// <summary>Gets the number of rooms.</summary>
    public int? Rooms { get; init; }

    /// <summary>Gets the area in square metres.</summary>
    public decimal? AreaM2 { get; init; }

    /// <summary>Gets the district.</summary>
    public string? District { get; init; }

    /// <summary>Gets the city.</summary>
    public string? City { get; init; }
}
