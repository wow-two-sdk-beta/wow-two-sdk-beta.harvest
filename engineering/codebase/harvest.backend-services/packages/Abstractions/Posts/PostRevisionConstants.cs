namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Holds the vocabulary of post revisions.</summary>
public static class PostRevisionConstants
{
    /// <summary>Holds the change name of a post's first revision.</summary>
    public const string Created = "created";

    /// <summary>Gets the base fields a revision never names: they record how a value was read, not the value.</summary>
    public static IReadOnlyList<string> IgnoredFields { get; } = ["extraction", "fieldSources", "flags"];
}
