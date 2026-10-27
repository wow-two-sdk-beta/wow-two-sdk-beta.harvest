using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

/// <summary>Declares the public direct-fetch plans for one cooperating web source family.</summary>
public sealed class GenericSourceAdapter : ISourceAdapter
{
    /// <summary>Creates an adapter for a generic page, structured page, whole site, feed or submitted link.</summary>
    /// <param name="family">The source family this adapter serves.</param>
    public GenericSourceAdapter(SourceFamily family)
    {
        if (family is not (SourceFamily.Page or SourceFamily.Structured or SourceFamily.Site or SourceFamily.Feed or SourceFamily.Link))
            throw new ArgumentOutOfRangeException(nameof(family));
        Family = family;
    }

    /// <inheritdoc />
    public string Key => $"generic-{Family.ToString().ToLowerInvariant()}";

    /// <inheritdoc />
    public SourceFamily Family { get; }

    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family;

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source) => new()
    {
        Steps =
        [
            new() { Key = "enumerate", Role = StepRole.Enumerate },
            new() { Key = "detail", Role = StepRole.Detail },
            new() { Key = "check", Role = StepRole.Check },
        ],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
        {
            [WorkKind.Capture] = Family == SourceFamily.Site ? ["enumerate", "detail"] : ["detail"],
            [WorkKind.Discover] = ["enumerate"],
            [WorkKind.Refresh] = ["detail"],
            [WorkKind.Sweep] = ["enumerate", "detail", "check"],
            [WorkKind.Check] = ["check"],
            [WorkKind.Link] = ["detail"],
        },
    };
}
