namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to the operator's switch on harvesting a source; the stricter of source and host wins.</summary>
public enum SourcePermission
{
    /// <summary>Harvest freely; the default.</summary>
    Open,

    /// <summary>Not scheduled until granted; manual entry only.</summary>
    Restricted,

    /// <summary>Permission was granted after a restriction.</summary>
    Granted,

    /// <summary>A takedown: nothing is scheduled.</summary>
    Denied,
}
