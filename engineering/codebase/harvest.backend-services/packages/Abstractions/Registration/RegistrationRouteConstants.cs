namespace WoW.Two.Sdk.Beta.Be.Harvest.Registration;

/// <summary>Holds the HTTP routes of the registration contract, shared by the service and the client.</summary>
public static class RegistrationRouteConstants
{
    /// <summary>Holds the tenants resource, relative to the service root.</summary>
    public const string Tenants = "api/tenants";

    /// <summary>Holds a tenant's API keys, under <see cref="Tenants"/>/{tenantId}.</summary>
    public const string TenantApiKeys = "api-keys";

    /// <summary>Holds the API keys resource for reads by key identifier.</summary>
    public const string ApiKeys = "api/api-keys";

    /// <summary>Holds the containers resource.</summary>
    public const string Containers = "api/containers";

    /// <summary>Holds the sources resource.</summary>
    public const string Sources = "api/sources";

    /// <summary>Holds the pipelines resource.</summary>
    public const string Pipelines = "api/pipelines";

    /// <summary>Holds the query parameter that filters containers by tenant.</summary>
    public const string TenantIdParameter = "tenantId";

    /// <summary>Holds the query parameter that filters sources and pipelines by container.</summary>
    public const string ContainerIdParameter = "containerId";
}
