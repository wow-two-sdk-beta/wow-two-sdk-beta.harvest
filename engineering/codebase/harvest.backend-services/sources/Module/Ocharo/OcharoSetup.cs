namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>One explicitly provisioned private Ocharo workspace, with no secrets in the result.</summary>
/// <param name="TenantId">The Ocharo tenant.</param>
/// <param name="ContainerId">The tenant's private event-candidate container.</param>
/// <param name="Sources">The pipelines and filtered pull routes.</param>
public sealed record OcharoSetup(Guid TenantId, Guid ContainerId, IReadOnlyList<OcharoSourceRegistration> Sources)
{
    /// <summary>Gets the tenant's <c>ocharo-events</c> selector subscription when setup was asked for it, else null.</summary>
    public Guid? SelectorSubscriptionId { get; init; }

    /// <summary>Gets the version of that subscription's selector as it stands, from 1; null when setup was not asked for it.</summary>
    public int? SelectorVersion { get; init; }

    /// <summary>Gets the keys (<c>ocharo.{groupId}</c>) of the catalog entries a setup that named no group left alone because the instance
    /// turned them off; empty when none was skipped.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];
}
