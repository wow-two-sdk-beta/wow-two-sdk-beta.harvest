using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Holds the vocabulary of the source catalog shared by the store, the catalogs and the hosts.</summary>
public static class SourceCatalogConstants
{
    /// <summary>Holds the reason a run, a capture write or a resume is refused for a source the instance turned off.</summary>
    public const string TurnedOffReason = "source-turned-off";

    /// <summary>Holds the <c>changed_by</c> of an entry a setup turned on.</summary>
    public const string ChangedBySetup = "setup";

    /// <summary>Holds the <c>changed_by</c> of an entry the boot reconcile found already set up.</summary>
    public const string ChangedByExisting = "existing";

    /// <summary>Holds the <c>changed_by</c> of a switch made with the operator API key.</summary>
    public const string ChangedByOperatorKey = "operator-key";

    /// <summary>Creates the error for work refused because the source's catalog entry is turned off.</summary>
    public static AppError TurnedOff() =>
        AppErrorFactory.Forbidden($"{TurnedOffReason}: The source is turned off on this instance.");
}
