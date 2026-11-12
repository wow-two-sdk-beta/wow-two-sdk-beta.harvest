namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>One explicitly provisioned private Ocharo workspace, with no secrets in the result.</summary>
/// <param name="TenantId">The Ocharo tenant.</param>
/// <param name="ContainerId">The tenant's private event-candidate container.</param>
/// <param name="Sources">The pipelines and filtered pull routes.</param>
public sealed record OcharoSetup(Guid TenantId, Guid ContainerId, IReadOnlyList<OcharoSourceRegistration> Sources);
