namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to a classification step's relevance verdict on a post, for the subscriber whose taxonomy it applied.</summary>
public enum ClassificationVerdict
{
    /// <summary>The post is relevant.</summary>
    Relevant,

    /// <summary>The post is not relevant.</summary>
    Irrelevant,

    /// <summary>The step could not decide; a consumer takes no decision from it.</summary>
    Uncertain,
}
