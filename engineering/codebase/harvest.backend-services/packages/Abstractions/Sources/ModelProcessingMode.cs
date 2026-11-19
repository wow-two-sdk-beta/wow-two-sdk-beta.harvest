namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Refers to how a model may process a source's posts: never, only when an operator runs it, or automatically.</summary>
/// <remarks>A post whose title or text comes from several sources takes the strictest of their modes.</remarks>
public enum ModelProcessingMode
{
    /// <summary>Nothing from the source is ever sent to a model: no queue, no operator run, no eval item.</summary>
    Off,

    /// <summary>Nothing queues on its own; an operator run that names the posts or lists the source, an eval item, or a
    /// Process stage started by hand may send its posts to a model.</summary>
    Manual,

    /// <summary>Post writes queue model processing, and scheduled Process stages may use a model.</summary>
    Auto,
}
