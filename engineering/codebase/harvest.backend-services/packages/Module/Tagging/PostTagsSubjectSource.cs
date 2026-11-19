using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Resolves <c>harvest:post:{id}@{revision}</c> eval subjects to the prompt variables of the use case the set
/// evaluates, once, when an eval item is added — exactly as the passes build them.</summary>
/// <remarks>
///   - <c>post-tags</c>: title, text (cut), the category hint, language, dates, place, publication date and taxonomy
///   - <c>extract-event</c> and <c>extract-listing</c>: the post fields with prices, and the done tags for the revision's
///     title and text; a subject whose tags name another entity type, or none yet, resolves to nothing
///   - the current post answers its own revision, or a subject with no revision; an older revision answers from a
///     delivered snapshot when one exists
///   - a post whose sources have model processing <c>off</c> resolves to nothing; <c>manual</c> resolves, as an operator
///     adding an item names the post
/// </remarks>
internal sealed class PostTagsSubjectSource(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    IOptions<PostTagsOptions> options,
    IOptions<PostExtractionOptions> extraction) : IEvalSubjectSource
{
    public string Prefix => PostTagsConstants.SubjectPrefix;

    public async Task<IReadOnlyDictionary<string, string?>?> ResolveAsync(string useCase, string subject, CancellationToken cancellationToken = default)
    {
        bool tagging = useCase == PostTagsConstants.UseCase;
        if (!tagging && !PostExtractionConstants.UseCases.Contains(useCase) || !TryParse(subject, out var postId, out var revision))
            return null;
        var post = await PostTagsStore.ReadSubjectAsync(dataSource, postId, cancellationToken);
        if (post is not { Explicit: true })
            return null;
        var envelope = revision is null || revision == post.Post.Revision ? post.Post.Base
            : await PostTagsStore.ReadDeliveredBaseAsync(dataSource, postId, revision.Value, cancellationToken);
        if (envelope is null)
            return null;
        var category = envelope.SourceTags.Count > 0 ? string.Join(" › ", envelope.SourceTags.Take(5)) : post.Section;
        if (tagging)
        {
            var taxonomy = await PostTagsStore.ReadTaxonomyAsync(dataSource, cancellationToken);
            return PostTagsContent.Variables(envelope, category, taxonomy.Rendered, options.Value.MaxTextChars, post.Post.FirstSeenAtUtc);
        }
        var tags = await PostExtractionStore.ReadTagsAsync(dataSource, postId, PostTagsContent.Hash(envelope), cancellationToken);
        return tags is not null && PostExtractionConstants.UseCaseFor(tags.EntityType) == useCase
            ? PostExtractionContent.Variables(envelope, category, tags, extraction.Value.MaxTextChars, post.Post.FirstSeenAtUtc)
            : null;
    }

    /// <summary>Parses <c>harvest:post:{id}</c> or <c>harvest:post:{id}@{revision}</c>.</summary>
    internal static bool TryParse(string? subject, out Guid postId, out int? revision)
    {
        postId = Guid.Empty;
        revision = null;
        if (subject is null || !subject.StartsWith(PostTagsConstants.SubjectPrefix, StringComparison.Ordinal))
            return false;
        var rest = subject.AsSpan(PostTagsConstants.SubjectPrefix.Length);
        int at = rest.IndexOf('@');
        if (at >= 0)
        {
            if (!int.TryParse(rest[(at + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                return false;
            revision = parsed;
            rest = rest[..at];
        }
        return Guid.TryParseExact(rest, "D", out postId);
    }
}
