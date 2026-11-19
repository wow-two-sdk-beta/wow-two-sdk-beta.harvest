namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Holds the entity routes, their query parameters and limits, shared by the service and the HTTP client.</summary>
public static class EntityRouteConstants
{
    /// <summary>Holds the entity collection route: search, and <c>{id}</c> beneath it.</summary>
    public const string Entities = "api/entities";

    /// <summary>Holds an entity's posts, under <c>api/entities/{id}</c>.</summary>
    public const string Posts = "posts";

    /// <summary>Holds the calendar of occurrences, under <c>api/entities</c>.</summary>
    public const string Calendar = "calendar";

    /// <summary>Holds the change feed, under <c>api/entities</c>.</summary>
    public const string Events = "events";

    /// <summary>Holds the search text parameter.</summary>
    public const string QueryParameter = "q";

    /// <summary>Holds the entity type parameter.</summary>
    public const string TypeParameter = "type";

    /// <summary>Holds the taxonomy domain parameter.</summary>
    public const string DomainParameter = "domain";

    /// <summary>Holds the year parameter of an entity's posts.</summary>
    public const string YearParameter = "year";

    /// <summary>Holds the role parameter of an entity's posts.</summary>
    public const string RoleParameter = "role";

    /// <summary>Holds the calendar window's first day.</summary>
    public const string FromParameter = "from";

    /// <summary>Holds the calendar window's last day.</summary>
    public const string ToParameter = "to";

    /// <summary>Holds the page size parameter.</summary>
    public const string LimitParameter = "limit";

    /// <summary>Holds the opaque cursor parameter.</summary>
    public const string CursorParameter = "cursor";

    /// <summary>Holds the search's default size.</summary>
    public const int DefaultSearchLimit = 20;

    /// <summary>Holds the search's largest size.</summary>
    public const int MaxSearchLimit = 100;

    /// <summary>Holds an entity's posts' default page size.</summary>
    public const int DefaultPostsLimit = 50;

    /// <summary>Holds an entity's posts' largest page size.</summary>
    public const int MaxPostsLimit = 200;

    /// <summary>Holds the change feed's default page size.</summary>
    public const int DefaultEventsLimit = 100;

    /// <summary>Holds the change feed's largest page size, as the subscription pull's.</summary>
    public const int MaxEventsLimit = 500;

    /// <summary>Holds the most days a calendar window spans after its first day.</summary>
    public const int MaxCalendarDays = 366;
}
