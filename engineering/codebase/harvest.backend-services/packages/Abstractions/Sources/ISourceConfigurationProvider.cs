namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>An optional adapter interface for safe, declarative console metadata.</summary>
public interface ISourceConfigurationProvider
{
    /// <summary>Describes local readiness and saved scope inputs without performing I/O or disclosing secrets.</summary>
    /// <param name="source">The source already authorized for the caller.</param>
    SourceConfigurationModel DescribeConfiguration(SourceModel source);
}
