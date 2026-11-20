using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Represents the tags the source sections of a post imply: the entity type and its taxonomy nodes, primary first.</summary>
internal sealed record ImpliedTags(string EntityType, IReadOnlyList<string> Nodes)
{
    /// <summary>Gets the row the post store keeps for these tags: no model, prompt version, English title or topics, and a
    /// timeframe of <c>evergreen</c>, as a listing is a standing offer.</summary>
    internal StoredPostTags ToStored() => new(EntityType, [.. Nodes.Select((node, index) => new PostTagsDomain(node, index == 0))], [], null,
        null, null, null, "evergreen", PostTagsConstants.SectionRule);
}

/// <summary>Reads which tags a post's title and text sources imply by the section each came from: a source adapter's
/// <see cref="ISourceSectionCatalog"/> declares them per section (<see cref="SectionImpliedTags"/>).</summary>
/// <remarks>
///   - a link's section is the pipeline of the run that made its newest capture, matched to the adapter's catalog by address
///     as <see cref="PostSections"/> does; a link with no run, an address no section names, or a section that implies nothing
///     leaves the post to the model
///   - the tags apply only when every link supplying the title and text declares them with one entity type; their nodes are
///     the sections' nodes, first link first
///   - a declaration naming an entity type no use case serves, or a node the taxonomy lacks or has switched off, is ignored
/// </remarks>
internal sealed class ImpliedTagResolver(IServiceProvider services)
{
    /// <summary>Resolves the tags the sections of <paramref name="linkIds"/> imply, or null when any of them implies none.</summary>
    /// <param name="connection">The connection the post write holds.</param>
    /// <param name="transaction">The write's transaction, or none.</param>
    /// <param name="containerId">The post's container.</param>
    /// <param name="linkIds">The links supplying the post's title and text.</param>
    /// <param name="ct">The token that cancels the read.</param>
    internal async Task<ImpliedTags?> ResolveAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid containerId,
        IReadOnlyList<Guid> linkIds, CancellationToken ct)
    {
        if (linkIds.Count == 0 || services.GetService<HarvestDbContext>() is not { } db)
            return null;
        var adapters = services.GetServices<ISourceAdapter>().Where(adapter => adapter is ISourceSectionCatalog).ToArray();
        if (adapters.Length == 0)
            return null;

        var addresses = new List<(Guid SourceId, string? Address)>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT l.source_id,pl.scope->>'url'
                         FROM harvest_post_links l
                         JOIN harvest_captures c ON c.container_id=l.container_id AND c.id=l.latest_capture_id AND c.captured_at=l.latest_captured_at
                         JOIN harvest_runs r ON r.id=c.run_id
                         JOIN harvest_pipelines pl ON pl.id=r.pipeline_id
                         WHERE l.container_id=@container AND l.id=ANY(@links)
                         """, ("container", containerId), ("links", linkIds.ToArray())))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                addresses.Add((reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        if (addresses.Count != linkIds.Count || addresses.Any(item => item.Address is null))
            return null;

        var declared = new List<SectionImpliedTags>();
        foreach (var (sourceId, address) in addresses)
        {
            if (await db.Sources.AsNoTracking().FirstOrDefaultAsync(source => source.Id == sourceId, ct) is not { } row)
                return null;
            var source = row.ToModel(null);
            if (adapters.LastOrDefault(adapter => adapter.Serves(source)) is not ISourceSectionCatalog catalog
                || catalog.Sections(source).FirstOrDefault(section => PostSections.SameSection(section.Url, address))?.ImpliedTags is not { } implied)
                return null;
            declared.Add(implied);
        }

        string entityType = declared[0].EntityType.Trim().ToLowerInvariant();
        if (PostExtractionConstants.UseCaseFor(entityType) is null
            || declared.Any(implied => implied.EntityType.Trim().ToLowerInvariant() != entityType))
            return null;
        var nodes = declared.Select(implied => implied.Domain.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        var known = new HashSet<string>(StringComparer.Ordinal);
        await using (var command = PostSql.Command(connection, transaction,
                         "SELECT key FROM harvest_taxonomy_nodes WHERE active AND key=ANY(@nodes)", ("nodes", nodes)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                known.Add(reader.GetString(0));
        return nodes.All(known.Contains) ? new ImpliedTags(entityType, nodes) : null;
    }
}
