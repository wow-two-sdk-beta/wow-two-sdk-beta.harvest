using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Resolves <c>harvest:post:{id}@{revision}</c> eval subjects to the post-tags prompt variables, once, when an
/// eval item is added: title, text (cut), the category hint, language, dates, place, publication date and taxonomy.</summary>
/// <remarks>The current post answers its own revision, or a subject with no revision; an older revision answers from a
/// delivered snapshot when one exists. A post whose sources do not allow AI resolves to nothing.</remarks>
internal sealed class PostTagsSubjectSource(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    IOptions<PostTagsOptions> options) : IEvalSubjectSource
{
    public string Prefix => PostTagsConstants.SubjectPrefix;

    public async Task<IReadOnlyDictionary<string, string?>?> ResolveAsync(string useCase, string subject, CancellationToken cancellationToken = default)
    {
        if (useCase != PostTagsConstants.UseCase || !TryParse(subject, out var postId, out var revision))
            return null;
        var post = await PostTagsStore.ReadSubjectAsync(dataSource, postId, cancellationToken);
        if (post is not { AiAllowed: true })
            return null;
        var envelope = revision is null || revision == post.Post.Revision ? post.Post.Base
            : await PostTagsStore.ReadDeliveredBaseAsync(dataSource, postId, revision.Value, cancellationToken);
        if (envelope is null)
            return null;
        var taxonomy = await PostTagsStore.ReadTaxonomyAsync(dataSource, cancellationToken);
        var category = envelope.SourceTags.Count > 0 ? string.Join(" › ", envelope.SourceTags.Take(5)) : post.Section;
        return PostTagsContent.Variables(envelope, category, taxonomy.Rendered, options.Value.MaxTextChars);
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
