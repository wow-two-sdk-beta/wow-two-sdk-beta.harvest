namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>An optional adapter interface for safe, declarative console metadata.</summary>
public interface ISourceConfigurationProvider
{
    /// <summary>Describes local readiness and saved scope inputs without performing I/O or disclosing secrets.</summary>
    /// <param name="source">The source already authorized for the caller.</param>
    SourceConfigurationModel DescribeConfiguration(SourceModel source);

    /// <summary>Reads a declared source-specific facet from the stored post, without outbound I/O.</summary>
    /// <param name="key">The declared facet identifier.</param>
    /// <param name="post">The stored summary for this source's post.</param>
    /// <param name="asOf">The reference instant used throughout the search.</param>
    /// <returns>The facet value, or null when the post has no applicable evidence.</returns>
    string? ReadPostFilter(string key, SourcePostModel post, DateTimeOffset asOf) => null;
}
