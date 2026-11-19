namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Configures the Layer 2 extraction, bound from <see cref="PostExtractionConstants.SectionName"/>.</summary>
/// <remarks>Extraction queues only for the pairs in <see cref="Wanted"/>, only when the host composes the shared AI
/// module, and only for posts whose title and text come from sources that allow AI.</remarks>
public sealed class PostExtractionOptions
{
    /// <summary>Gets or sets the (entity type, domain) pairs a subscriber wants extracted, until the subscription selector
    /// supplies demand; empty queues nothing.</summary>
    public List<PostExtractionWant> Wanted { get; set; } = [];

    /// <summary>Gets or sets the model routes to try in order, each <c>{provider}/{model}</c>; empty takes
    /// <see cref="PostExtractionConstants.DefaultModel"/>.</summary>
    public string[] Models { get; set; } = [];

    /// <summary>Gets or sets a prompt version to pin per use case; a use case absent here takes its production label,
    /// else its newest version.</summary>
    public Dictionary<string, int> PromptVersions { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets how many characters of the post's text the prompt receives.</summary>
    public int MaxTextChars { get; set; } = 6000;

    /// <summary>Gets or sets how many queued extractions one worker pass claims.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Gets or sets how long the worker waits when nothing is due.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the base delay before a failed or unavailable answer is retried; it grows with each attempt.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets how long a claim holds a post before another worker may take it.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
