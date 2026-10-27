namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Holds the scopes an API key may grant: operator scopes for the registry, tenant scopes for its own work.</summary>
public static class HarvestScopeConstants
{
    /// <summary>Holds the scope that registers tenants and issues and revokes their keys (operator).</summary>
    public const string TenantsManage = "tenants:manage";

    /// <summary>Holds the scope that registers containers (operator).</summary>
    public const string ContainersManage = "containers:manage";

    /// <summary>Holds the scope that registers sources (operator).</summary>
    public const string SourcesManage = "sources:manage";

    /// <summary>Holds the scope that reads pipelines.</summary>
    public const string PipelinesRead = "pipelines:read";

    /// <summary>Holds the scope that starts runs of pipelines.</summary>
    public const string PipelinesRun = "pipelines:run";

    /// <summary>Holds the scope that declares and changes pipelines.</summary>
    public const string PipelinesManage = "pipelines:manage";

    /// <summary>Holds the scope that creates and changes subscriptions.</summary>
    public const string SubscriptionsManage = "subscriptions:manage";

    /// <summary>Holds the scope that reads a subscription's events.</summary>
    public const string EventsRead = "events:read";

    /// <summary>Holds the scope that submits links.</summary>
    public const string LinksSubmit = "links:submit";

    /// <summary>Holds the scope that writes manual entries.</summary>
    public const string EntriesWrite = "entries:write";

    /// <summary>Holds the scope that reads posts.</summary>
    public const string PostsRead = "posts:read";

    /// <summary>Gets the scopes only the operator key holds; a tenant key never grants one.</summary>
    public static IReadOnlyList<string> OperatorScopes { get; } = [TenantsManage, ContainersManage, SourcesManage];

    /// <summary>Gets the scopes a tenant key may grant.</summary>
    public static IReadOnlyList<string> TenantScopes { get; } =
    [
        PipelinesRead, PipelinesRun, PipelinesManage, SubscriptionsManage, EventsRead, LinksSubmit, EntriesWrite, PostsRead,
    ];

    /// <summary>Gets every scope, which the operator key holds.</summary>
    public static IReadOnlyList<string> All { get; } = [.. OperatorScopes, .. TenantScopes];
}
