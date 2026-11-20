namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Catalog;

/// <summary>Holds the SQL that states "this source is turned off", once, for every gate that reads it when it acts.</summary>
/// <remarks>A source is off when <c>harvest_catalog_sources</c> holds <c>turned_on = false</c> for its <c>catalog_key</c>; a source with
/// no key, or an entry with no record, is on.</remarks>
internal static class CatalogGate
{
    /// <summary>The condition, over source alias <c>s</c>, that the instance turned the source's catalog entry off.</summary>
    internal const string SourceOff =
        "EXISTS(SELECT 1 FROM harvest_catalog_sources cg WHERE cg.key=s.catalog_key AND NOT cg.turned_on)";

    /// <summary>Returns the same condition over another source alias.</summary>
    /// <param name="alias">The alias of <c>harvest_sources</c> in the query.</param>
    internal static string Off(string alias) =>
        $"EXISTS(SELECT 1 FROM harvest_catalog_sources cg WHERE cg.key={alias}.catalog_key AND NOT cg.turned_on)";

    /// <summary>The condition, over the parameter <c>@post</c>, that the post has links and every one of them belongs to a source the
    /// instance turned off: delivery writes no event for such a post.</summary>
    internal const string PostOff = """
        (EXISTS(SELECT 1 FROM harvest_post_links gl WHERE gl.post_id=@post AND gl.retired_at IS NULL)
         AND NOT EXISTS(SELECT 1 FROM harvest_post_links gl JOIN harvest_sources s ON s.id=gl.source_id
             WHERE gl.post_id=@post AND gl.retired_at IS NULL AND NOT EXISTS(SELECT 1 FROM harvest_catalog_sources cg WHERE cg.key=s.catalog_key AND NOT cg.turned_on)))
        """;
}
