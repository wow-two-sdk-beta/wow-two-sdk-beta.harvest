using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Names the harvest section a post's text came from, for the <c>{{section}}</c> variable of an <c>extract-listing</c>
/// prompt: the source adapter's catalog key (<c>apartments-rent</c>, <c>houses-rent</c>, <c>daily-rent</c> for OLX) when the
/// pipeline's address is one of the catalog's sections, else the pipeline's own name, else none.</summary>
/// <remarks>The catalog is the adapter's (<see cref="ISourceSectionCatalog"/>), read for the post's source as the Posts query
/// reads it; a host without the source adapters or without the module's database context falls back to the pipeline's name.</remarks>
internal sealed class PostSections(IServiceProvider services)
{
    /// <summary>Names the section of <paramref name="subject"/>.</summary>
    /// <param name="subject">The post the pass reads.</param>
    /// <param name="ct">The token that cancels the read.</param>
    internal async Task<string?> NameAsync(PostTagsSubject subject, CancellationToken ct)
    {
        if (subject.SectionUrl is not { } address || subject.SourceId is not { } sourceId
            || services.GetService<HarvestDbContext>() is not { } db)
            return subject.Section;
        var row = await db.Sources.AsNoTracking().FirstOrDefaultAsync(source => source.Id == sourceId, ct);
        if (row is null)
            return subject.Section;
        var source = row.ToModel(null);
        var adapter = services.GetServices<ISourceAdapter>().LastOrDefault(candidate => candidate.Serves(source));
        return Key(adapter as ISourceSectionCatalog, source, address) ?? subject.Section;
    }

    /// <summary>Returns the key of the catalog section whose address is <paramref name="address"/>, or null.</summary>
    /// <param name="catalog">The adapter's catalog, or none.</param>
    /// <param name="source">The source the post came from.</param>
    /// <param name="address">The pipeline's scope address.</param>
    internal static string? Key(ISourceSectionCatalog? catalog, SourceModel source, string address) =>
        catalog?.Sections(source).FirstOrDefault(section => SameSection(section.Url, address))?.Key;

    /// <summary>Whether two addresses name one section: the same host without <c>www.</c>, the same path and query, a trailing
    /// slash aside.</summary>
    internal static bool SameSection(string expected, string? actual) =>
        Uri.TryCreate(expected, UriKind.Absolute, out var left) && Uri.TryCreate(actual, UriKind.Absolute, out var right)
        && Host(left) == Host(right) && left.PathAndQuery.TrimEnd('/') == right.PathAndQuery.TrimEnd('/');

    private static string Host(Uri uri) => uri.IdnHost.StartsWith("www.", StringComparison.Ordinal) ? uri.IdnHost[4..] : uri.IdnHost;
}
