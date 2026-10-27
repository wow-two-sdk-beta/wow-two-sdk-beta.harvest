using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Profiles;

/// <summary>Resolves request profiles by name.</summary>
public interface IProfileCatalog
{
    /// <summary>Gets the names the catalog serves.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>Resolves a profile.</summary>
    /// <param name="name">The profile's name.</param>
    /// <returns>The profile; <c>NotFound</c> for a name the catalog does not serve.</returns>
    Result<RequestProfile> Resolve(string name);
}
