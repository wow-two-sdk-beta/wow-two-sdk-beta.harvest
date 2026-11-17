using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Resolves a post budget while keeping provider pagination internal.</summary>
public static class InstagramCollectionLimits
{
    /// <summary>The most media records requested in one provider call.</summary>
    public const int RequestSize = 50;

    /// <summary>The default number of recent posts examined by one run.</summary>
    public const int DefaultPosts = 100;

    /// <summary>The internal page guard when short provider pages cannot satisfy the post budget.</summary>
    public const int MaximumPages = PipelineConstants.MaxPagesCap;

    /// <summary>Reads the saved post budget or preserves an older pipeline's effective page-based bound.</summary>
    /// <param name="limits">The stored or frozen pipeline limits.</param>
    /// <returns>The explicit post budget, or the legacy page-size product capped by the engine's item maximum.</returns>
    public static int PostsPerRun(PipelineLimits limits) => limits.MaxItems
        ?? (int)Math.Min(PipelineConstants.MaxItemsCap, (long)(limits.PageSize ?? 25) * (limits.MaxPages ?? 4));
}
