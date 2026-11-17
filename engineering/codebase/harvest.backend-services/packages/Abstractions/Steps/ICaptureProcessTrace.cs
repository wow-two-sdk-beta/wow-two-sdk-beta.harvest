namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Records operations from a capture processor in the run audit.</summary>
public interface ICaptureProcessTrace
{
    /// <summary>Runs and records one operation. The description must exclude reading or model content.</summary>
    Task<T> RunOperationAsync<T>(string key, Func<Task<T>> execute, Func<T, StepOperationResult>? describe = null);
}
