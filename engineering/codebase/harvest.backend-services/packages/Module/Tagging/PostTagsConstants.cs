namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Holds the names the post-tags pass shares with hosts, prompts and subscribers.</summary>
public static class PostTagsConstants
{
    /// <summary>The shared AI use case, versioned, whose prompt the operator registers.</summary>
    public const string UseCase = "post-tags";

    /// <summary>The post work kind in <c>harvest_post_work</c>.</summary>
    public const string WorkKind = "post-tags";

    /// <summary>The eval subject prefix: <c>harvest:post:{id}@{revision}</c>.</summary>
    public const string SubjectPrefix = "harvest:post:";

    /// <summary>The host configuration section of <see cref="PostTagsOptions"/>.</summary>
    public const string SectionName = "Harvest:Processing:Tags";

    /// <summary>The model route used when the host names none.</summary>
    public const string DefaultModel = "anthropic/claude-haiku-5-5";

    /// <summary>The <c>PostEnvelope.Extras</c> namespace the pass writes, holding <c>titleEn</c>.</summary>
    public const string ExtrasNamespace = "postTags";

    /// <summary>The tag prefix of the entity type: <c>type:event</c>, <c>type:listing</c>, <c>type:none</c>.</summary>
    public const string TypeTagPrefix = "type:";

    /// <summary>The tag prefix of a taxonomy node: <c>domain:style-fashion</c>, <c>domain:style-fashion/womens-fashion</c>.</summary>
    public const string DomainTagPrefix = "domain:";

    /// <summary>The tag prefix of an open English topic: <c>topic:textile</c>.</summary>
    public const string TopicTagPrefix = "topic:";
}
