using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents the base envelope <c>wow2.post.v1</c>: what extraction makes of one capture, whatever the source.</summary>
/// <remarks>Every field but the schema is nullable or empty: a missing value is null, never a default. Text stays in the
/// source language; nothing is translated or generated. A product breaks the fields down its own way.</remarks>
public sealed record PostEnvelope
{
    /// <summary>Gets the envelope's schema, <see cref="PostEnvelopeConstants.Schema"/>.</summary>
    public string Schema { get; init; } = PostEnvelopeConstants.Schema;

    /// <summary>Gets what the post is.</summary>
    public PostKind? Kind { get; init; }

    /// <summary>Gets the title, verbatim; null for channel posts, never generated.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the text, verbatim in Unicode NFC, with HTML flattened.</summary>
    public string? Text { get; init; }

    /// <summary>Gets who published the post.</summary>
    public PostAuthor? Author { get; init; }

    /// <summary>Gets when the post was published, not when its event happens.</summary>
    public PostTimestamp? PostedAt { get; init; }

    /// <summary>Gets the post's language and script.</summary>
    public PostLanguage? Language { get; init; }

    /// <summary>Gets the post's media.</summary>
    public IReadOnlyList<PostMedia> Media { get; init; } = [];

    /// <summary>Gets the links the post contains.</summary>
    public IReadOnlyList<PostLink> Links { get; init; } = [];

    /// <summary>Gets where the post is about.</summary>
    public PostLocation? Location { get; init; }

    /// <summary>Gets the prices the post states.</summary>
    public IReadOnlyList<PostPrice> Prices { get; init; } = [];

    /// <summary>Gets the dates the post states.</summary>
    public IReadOnlyList<PostDate> Dates { get; init; } = [];

    /// <summary>Gets the contact points the post gives.</summary>
    public IReadOnlyList<PostContact> Contacts { get; init; } = [];

    /// <summary>Gets the tags from the shared vocabulary.</summary>
    public IReadOnlyList<PostTag> Tags { get; init; } = [];

    /// <summary>Gets a snapshot of the post's counters.</summary>
    public PostEngagement? Engagement { get; init; }

    /// <summary>Gets one namespace per source family: <c>telegram</c>, <c>schemaOrg</c>, <c>openGraph</c>, <c>listing</c>,
    /// <c>social</c>, <c>feed</c>.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extras { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>Gets where each field came from, by field name.</summary>
    public IReadOnlyDictionary<string, FieldSource> FieldSources { get; init; } = new Dictionary<string, FieldSource>();

    /// <summary>Gets the flags, such as <c>low_confidence</c> or <c>ai_skipped:{gate}</c>.</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>Gets what extraction ran.</summary>
    public required ExtractionStamp Extraction { get; init; }
}
