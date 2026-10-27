using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Reads one kind of source by declaring steps; a generic family reader serves every source of its family, a
/// named-source broker serves its own hosts.</summary>
/// <remarks>Named-source brokers live in a private repository, one package per source, and reference only this
/// contract and core; a host registers each in code.</remarks>
public interface ISourceAdapter
{
    /// <summary>Gets the adapter's stable key, such as <c>jsonld-event</c> or <c>olx-uz</c>.</summary>
    string Key { get; }

    /// <summary>Gets the family the adapter reads.</summary>
    SourceFamily Family { get; }

    /// <summary>Tells whether the adapter serves a source; the most specific serving adapter wins.</summary>
    /// <param name="source">The registered source.</param>
    /// <returns><c>true</c> when the adapter can read the source.</returns>
    bool Serves(SourceModel source);

    /// <summary>Declares the adapter's steps and plans for a source.</summary>
    /// <param name="source">The registered source, with its capability policy.</param>
    /// <returns>The steps and plans, before the source's and the pipeline's layers.</returns>
    AdapterDescriptor Describe(SourceModel source);
}
