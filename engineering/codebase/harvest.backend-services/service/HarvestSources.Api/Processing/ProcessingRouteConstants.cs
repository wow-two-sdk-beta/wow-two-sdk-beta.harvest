namespace HarvestSources.Api.Processing;

/// <summary>Holds the routes of the AI operator controls; each has a <c>processing</c> host verb of the same name.</summary>
public static class ProcessingRouteConstants
{
    /// <summary>Holds the root of the operator controls.</summary>
    public const string Root = "api/processing";

    /// <summary>Holds the re-runs: <c>POST</c> queues or dry-runs one, <c>GET {runId}</c> reads its progress.</summary>
    public const string Reruns = Root + "/reruns";

    /// <summary>Holds the eval sets built from stored posts: <c>POST</c> fills one.</summary>
    public const string EvalSets = Root + "/eval-sets";

    /// <summary>Holds the AI spend guard: <c>GET</c> reads the current window's spend against its limit.</summary>
    public const string Budget = Root + "/budget";
}
