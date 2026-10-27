namespace WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

/// <summary>Refers to whether a tenant may use the service.</summary>
public enum TenantState
{
    /// <summary>The tenant's keys authenticate and its pipelines run.</summary>
    Active,

    /// <summary>The operator stopped the tenant; its data stays.</summary>
    Suspended,
}
