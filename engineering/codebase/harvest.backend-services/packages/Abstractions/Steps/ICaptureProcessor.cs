namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Interprets a saved source reading without recapturing its page.</summary>
public interface ICaptureProcessor
{
    /// <summary>The built-in model-free summary.</summary>
    public const string DefaultKey = "saved-reading@1";

    /// <summary>Gets the stable key under which the host registers this processor.</summary>
    string Key { get; }

    /// <summary>Gets the processor's operations in execution order, or an empty list for one operation.</summary>
    IReadOnlyList<string> OperationKeys => [];

    /// <summary>Processes an existing reading and returns the result to store.</summary>
    Task<CaptureProcessResult> ProcessAsync(CaptureProcessInput input, CancellationToken cancellationToken = default);
}
