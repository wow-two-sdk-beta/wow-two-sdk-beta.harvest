namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Refers to how a discover stage walks its section's catalog on each run.</summary>
public enum DiscoveryWalk
{
    /// <summary>Resume at the catalog cursor the last run left; a finished cycle starts again at the first page. The default.</summary>
    Resume,

    /// <summary>Clear the cursor and read every catalog page from the first to the last, whatever the pipeline's request budget.</summary>
    Full,

    /// <summary>Clear the cursor and read from the first page, stopping at the first page that saves no new discovery: every
    /// listing on it is already held. The pipeline's request budget does not cut it.</summary>
    New,
}
