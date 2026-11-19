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

    /// <summary>Holds the paged Posts query beneath a source's posts collection.</summary>
    public const string PostQuery = Posts + "/query";

    /// <summary>Holds a source's processed posts, under <c>api/sources/{sourceId}</c>.</summary>
    public const string Processed = "processed";

    /// <summary>Holds the processed posts' entity type parameter.</summary>
    public const string EntityTypeParameter = "entityType";

    /// <summary>Holds the processed posts' verdict parameter.</summary>
    public const string VerdictParameter = "verdict";

    /// <summary>Holds the processed posts' has-extraction parameter.</summary>
    public const string ExtractedParameter = "extracted";

    /// <summary>Holds the opaque cursor parameter.</summary>
    public const string CursorParameter = "cursor";

    /// <summary>Holds a source's reading pace, under <c>api/sources/{sourceId}</c>.</summary>
    public const string Reading = "reading";

    /// <summary>Holds a source's authorized media route, under <c>api/sources/{sourceId}</c>.</summary>
    public const string Assets = "assets";

    /// <summary>Returns the relative address of a stored source asset.</summary>
    public static string AssetUrl(Guid sourceId, Guid assetId) =>
        $"/api/sources/{sourceId:D}/{Assets}/{assetId:D}";

    /// <summary>Holds a post's revisions, under <c>api/posts/{postId}</c>.</summary>
    public const string Revisions = "revisions";

    /// <summary>Holds the query parameter that caps a list.</summary>
    public const string LimitParameter = "limit";
}
