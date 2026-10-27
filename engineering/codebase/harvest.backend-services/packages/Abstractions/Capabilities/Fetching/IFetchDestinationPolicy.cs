namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Restricts a source's page destinations before transport, including every redirect hop.</summary>
public interface IFetchDestinationPolicy
{
    /// <summary>Tells whether the source permits fetching this public page destination.</summary>
    /// <param name="destination">The initial or redirected page URL.</param>
    /// <returns>Whether the page belongs to the source's supported public routes.</returns>
    bool Allows(Uri destination);
}
