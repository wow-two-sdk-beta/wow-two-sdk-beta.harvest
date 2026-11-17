namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Refers to whether the call a deadline closes takes applications, as the source states it.</summary>
public enum CallStatus
{
    /// <summary>The source does not say.</summary>
    Unknown,

    /// <summary>The source says applications are open.</summary>
    Open,

    /// <summary>The source says applications are closed.</summary>
    Closed,
}
