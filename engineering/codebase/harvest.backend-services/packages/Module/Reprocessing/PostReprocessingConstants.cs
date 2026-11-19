using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;

/// <summary>Holds the names and limits operator re-runs and eval sets share with hosts.</summary>
public static class PostReprocessingConstants
{
    /// <summary>The scope of a run over named posts.</summary>
    public const string PostsScope = "posts";

    /// <summary>The scope of a run over listed sources.</summary>
    public const string SourcesScope = "sources";

    /// <summary>The scope of a run over every source.</summary>
    public const string AllScope = "all";

    /// <summary>The most posts one run names.</summary>
    public const int MaxPosts = 1000;

    /// <summary>The most sources one run or eval set lists.</summary>
    public const int MaxSources = 100;

    /// <summary>The most posts one eval set call samples.</summary>
    public const int MaxSample = 500;

    /// <summary>Expected answers start as an empty object.</summary>
    public const string ExpectedEmpty = "empty";

    /// <summary>Expected answers start as the post's current done answer.</summary>
    public const string ExpectedCurrent = "current";

    /// <summary>The tag every item an eval set call adds carries until someone reviews its expected answer.</summary>
    public const string UnreviewedTag = "unreviewed";

    /// <summary>Who an expected answer taken from the current done answer is labelled by.</summary>
    public const string CurrentAnswerLabel = "harvest:current-answer";

    /// <summary>Gets the use cases a run may name, tagging first.</summary>
    public static IReadOnlyList<string> UseCases { get; } =
        [PostTagsConstants.UseCase, PostExtractionConstants.EventUseCase, PostExtractionConstants.ListingUseCase];
}
