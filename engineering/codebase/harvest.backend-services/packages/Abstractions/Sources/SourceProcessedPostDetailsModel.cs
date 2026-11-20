namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents one processed post opened in full: the post as the processed list shows it, beside what the list
/// leaves out — the quotes behind each extracted date, price and rental term, and each pass's run.</summary>
/// <remarks>Every tag, the verdict with its reason and confidence, and the unsure flags already ride in
/// <see cref="Post"/>; a list card only cuts them for room.</remarks>
public sealed record SourceProcessedPostDetailsModel
{
    /// <summary>Gets the post with its tags, verdict and extraction, exactly as the processed list reads it.</summary>
    public required SourceProcessedPostModel Post { get; init; }

    /// <summary>Gets the quotes the extraction answer keeps behind its fields; none without an extraction, or when the
    /// answer stores none.</summary>
    public IReadOnlyList<ProcessedEvidenceModel> Evidence { get; init; } = [];

    /// <summary>Gets each AI pass the post carries, the tags pass first.</summary>
    public IReadOnlyList<ProcessedPassModel> Passes { get; init; } = [];
}

/// <summary>Represents the verbatim quotes an extraction answer keeps behind one field, with how sure the answer is.</summary>
/// <param name="Field">The field's JSON path, as the flags name it: <c>occurrences[0]</c>, <c>prices[1]</c>,
/// <c>registration.opens</c>, <c>registration.deadline</c>, <c>price</c> or <c>rentalTerms.deposit</c>.</param>
/// <param name="Value">The value in short words, for a field the post's extraction summary does not carry (registration
/// dates and rental terms); none for an occurrence or a price, whose value the summary holds.</param>
/// <param name="Certainty">How the answer knows it: <c>stated</c> or <c>estimated</c> for an occurrence, <c>present</c>,
/// <c>absent</c> or <c>conflict</c> for a rental term; none for a price or a registration date.</param>
/// <param name="Quotes">The quotes copied from the post, one per claim.</param>
public sealed record ProcessedEvidenceModel(string Field, string? Value, string? Certainty, IReadOnlyList<string> Quotes);

/// <summary>Represents one AI pass over a post: which pass, how it ended, who answered and the inference it ran as.</summary>
public sealed record ProcessedPassModel
{
    /// <summary>Gets the pass: <c>post-tags</c>, <c>extract-event</c> or <c>extract-listing</c>.</summary>
    public required string Pass { get; init; }

    /// <summary>Gets the pass's status: <c>done</c>, <c>failed</c> or <c>unavailable</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets why a failed or unavailable pass has no answer.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the AI module's inference id the answer ran as, its run in the inference log; none for a form that
    /// keeps no inference log.</summary>
    public Guid? InferenceId { get; init; }

    /// <summary>Gets who answered: provider, model, prompt version, cost, latency, tokens and when.</summary>
    public required ProcessedAnswerModel Answer { get; init; }
}
