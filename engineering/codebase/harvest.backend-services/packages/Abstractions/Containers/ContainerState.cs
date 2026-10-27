namespace WoW.Two.Sdk.Beta.Be.Harvest.Containers;

/// <summary>Refers to whether a container takes new sources and pipelines.</summary>
public enum ContainerState
{
    /// <summary>The container is in use.</summary>
    Active,

    /// <summary>The container is closed; its posts follow their retention.</summary>
    Archived,
}
