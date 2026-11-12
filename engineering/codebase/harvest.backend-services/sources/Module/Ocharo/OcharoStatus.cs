using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>What setup owns on a target, read without writing: the tenant, its container and each registry channel's
/// pipeline.</summary>
/// <param name="TenantId">The Ocharo tenant.</param>
/// <param name="ContainerId">The tenant's private event-candidate container.</param>
/// <param name="Channels">One row per selected registry channel, in registry order.</param>
public sealed record OcharoStatus(Guid TenantId, Guid ContainerId, IReadOnlyList<OcharoChannelStatus> Channels);

/// <summary>One registry channel and its pipeline, or none when setup has not created it.</summary>
/// <param name="GroupId">The publisher in the registry.</param>
/// <param name="ChannelId">The channel in the publisher.</param>
/// <param name="Pipeline">The channel's pipeline, with its schedules.</param>
public sealed record OcharoChannelStatus(string GroupId, string ChannelId, PipelineModel? Pipeline);
