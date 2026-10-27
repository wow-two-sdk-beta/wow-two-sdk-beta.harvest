namespace WoW.Two.Sdk.Beta.Be.Harvest.Containers;

/// <summary>Refers to a container's visibility: the shared pool or one tenant's own.</summary>
public enum ContainerKind
{
    /// <summary>Everything harvested publicly under the operator's pipelines; one per instance, read by every tenant.</summary>
    Pool,

    /// <summary>One tenant's own posts: submitted links, entries and credentialed sources; never merged into the pool.</summary>
    Private,
}
