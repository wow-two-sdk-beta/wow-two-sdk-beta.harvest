namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Holds the HTTP routes of the console's Sources area, shared by the service and the client.</summary>
public static class SourceRouteConstants
{
    /// <summary>Holds the overview of a container's sources, read with <c>containerId</c>.</summary>
    public const string Overview = "api/sources/overview";

    /// <summary>Holds the source groups resource, listed with <c>containerId</c>.</summary>
    public const string Groups = "api/sources/groups";

    /// <summary>Holds a source's posts, under <c>api/sources/{sourceId}</c>.</summary>
    public const string Posts = "posts";

    /// <summary>Holds a source's reading pace, under <c>api/sources/{sourceId}</c>.</summary>
    public const string Reading = "reading";

    /// <summary>Holds a post's revisions, under <c>api/posts/{postId}</c>.</summary>
    public const string Revisions = "revisions";

    /// <summary>Holds the query parameter that caps a list.</summary>
    public const string LimitParameter = "limit";
}
