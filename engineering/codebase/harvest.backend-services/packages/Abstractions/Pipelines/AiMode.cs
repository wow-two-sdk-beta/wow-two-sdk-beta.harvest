namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Refers to a pipeline's AI switch; the deterministic and rule tiers always run.</summary>
public enum AiMode
{
    /// <summary>Deterministic and rule tiers only; the default.</summary>
    Off,

    /// <summary>Model tiers fill only what the earlier tiers left missing or unsure.</summary>
    Gaps,

    /// <summary>Model tiers run on every in-scope post.</summary>
    Full,
}
