namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Refers to what a step learned about a known link's existence.</summary>
public enum LinkSignal
{
    /// <summary>The link answered.</summary>
    Present,

    /// <summary>A weak negative: absent from a complete enumeration, a soft 404, a vanished node.</summary>
    WeakNegative,

    /// <summary>A strong negative: 410, 404 on a direct fetch, a named not-found redirect.</summary>
    StrongNegative,
}
