namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Holds the event types a subscription's log carries, as they travel on the wire.</summary>
public static class DeliveryEventTypes
{
    /// <summary>Holds the type of a post the subscription sees for the first time.</summary>
    public const string PostNew = "post.new";

    /// <summary>Holds the type of a new revision of a post the subscription holds.</summary>
    public const string PostChanged = "post.changed";

    /// <summary>Holds the type of an extraction that finished after its capture was delivered.</summary>
    public const string PostExtracted = "post.extracted";

    /// <summary>Holds the type of a link moving between live, suspect, gone and relisted.</summary>
    public const string LinkState = "link.state";

    /// <summary>Holds the type of a finished run, so a consumer tells "nothing new" from "unchecked".</summary>
    public const string PipelineRun = "pipeline.run";

    /// <summary>Holds the type of a pipeline's health change: blocked, restricted, unavailable, over budget.</summary>
    public const string PipelineHealth = "pipeline.health";

    /// <summary>Holds the type of a merge, carrying the loser and the survivor.</summary>
    public const string PostMerged = "post.merged";

    /// <summary>Holds the type of an undone merge.</summary>
    public const string PostUnmerged = "post.unmerged";

    /// <summary>Holds the type of a purged post, which a consumer must honour.</summary>
    public const string PostPurged = "post.purged";

    /// <summary>Holds the type of a post that came back after it was gone.</summary>
    public const string PostRelisted = "post.relisted";

    /// <summary>Gets every type, in the order above.</summary>
    public static IReadOnlyList<string> All { get; } =
        [PostNew, PostChanged, PostExtracted, LinkState, PipelineRun, PipelineHealth, PostMerged, PostUnmerged, PostPurged, PostRelisted];
}
