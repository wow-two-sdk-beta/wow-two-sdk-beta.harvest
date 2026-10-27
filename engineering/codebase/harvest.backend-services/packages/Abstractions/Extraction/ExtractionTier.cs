namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to a tier of the extraction cascade, run in order; a later tier fills only what is missing.</summary>
public enum ExtractionTier
{
    /// <summary>API fields, structured data, per-source parsers, feeds; always runs.</summary>
    Deterministic,

    /// <summary>Regex, lexicons, gazetteer, script and language rules; always runs.</summary>
    Rules,

    /// <summary>Embeddings and nearest-centroid tags; behind the AI gates.</summary>
    Embeddings,

    /// <summary>A small model; behind the AI gates.</summary>
    SmallModel,

    /// <summary>A large model; behind the AI gates.</summary>
    LargeModel,
}
