using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents the hosts outside its own that serve a source's post images, and how many of a post's images are
/// worth copying from them.</summary>
/// <remarks>A source that declares none copies only the images served from its own host. The declaration widens where a
/// copy may come from; it never relaxes the scheme, port, credential, public-address, redirect or size checks.</remarks>
public sealed record SourceMediaHosts
{
    /// <summary>Gets the registrable domains, lower-case with no scheme and no leading dot, whose host or any subdomain
    /// serves the source's post images, such as <c>olxcdn.com</c>.</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>Gets how many of a post's first image positions are copied.</summary>
    public int Limit { get; init; } = SourceConstants.MaxPostImages;

    /// <summary>Gets the least gap between these copies' requests to one image host.</summary>
    public TimeSpan MinDelay { get; init; } = TimeSpan.FromSeconds(2);
}
