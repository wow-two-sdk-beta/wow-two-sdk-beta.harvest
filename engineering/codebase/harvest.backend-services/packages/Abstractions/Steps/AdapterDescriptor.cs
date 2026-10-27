using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what an adapter declares for a source: its steps and the step plan of each work kind.</summary>
public sealed record AdapterDescriptor
{
    /// <summary>Gets the steps the adapter offers.</summary>
    public required IReadOnlyList<StepDescriptor> Steps { get; init; }

    /// <summary>Gets each work kind's plan: the step keys it runs, in order, such as capture →
    /// <c>enumerate, detail, contact, media, extract</c>.</summary>
    public required IReadOnlyDictionary<WorkKind, IReadOnlyList<string>> Plans { get; init; }
}
