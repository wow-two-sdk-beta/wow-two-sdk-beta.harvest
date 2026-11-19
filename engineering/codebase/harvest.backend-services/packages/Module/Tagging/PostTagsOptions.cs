namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Configures the post-tags pass, bound from <see cref="PostTagsConstants.SectionName"/>.</summary>
/// <remarks>The pass queues and runs only when <see cref="Enabled"/> is set and the host composes the shared AI module,
/// and only for posts whose title and text come from sources that allow AI.</remarks>
public sealed class PostTagsOptions
{
    /// <summary>Gets or sets whether post writes queue tagging and the worker runs it.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how many characters of the post's text the prompt receives.</summary>
    public int MaxTextChars { get; set; } = 4000;

    /// <summary>Gets or sets the model routes to try in order, each <c>{provider}/{model}</c>; empty takes
    /// <see cref="PostTagsConstants.DefaultModel"/>.</summary>
    public string[] Models { get; set; } = [];

    /// <summary>Gets or sets a prompt version to pin; none takes the use case's production label, else its newest.</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Gets or sets how many queued posts one worker pass claims.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Gets or sets how long the worker waits when nothing is due.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets the base delay before a failed or unavailable answer is retried; it grows with each attempt.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Gets or sets how long a claim holds a post before another worker may take it.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
