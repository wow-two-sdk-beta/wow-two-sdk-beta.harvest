using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HarvestSources.Api.Configurations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace HarvestSources.Api.Media;

/// <summary>Answers <c>assets report</c>: reads each source's image copies by state, why the rest did not copy, the bytes of each
/// copy, and how many of its live posts carry a contact, from the deployed host, so a platform job reads the counts with no
/// database access and no operator token.</summary>
/// <remarks>
///   - <c>assets report [--sources &lt;id,id&gt;]</c> writes nothing (one read-only, repeatable-read transaction), prints one JSON line and
///     exits <c>0</c>: <c>generatedAt</c>, <c>sources</c> and <c>totals</c>; <c>--sources</c> limits it to the named sources
///   - a source is listed when it is active and has any asset job or any linked post: <c>sourceId</c>, <c>handle</c>, <c>family</c>,
///     <c>images</c> (<c>jobs</c>, <c>copied</c>, <c>due</c> not copied and due to retry, <c>deferred</c> not copied and waiting,
///     <c>byOutcome</c> per recorded outcome, <c>unrecorded</c> for an attempt older than <c>020-asset-outcomes</c> or not yet made),
///     <c>bytes</c> of the copied images (<c>sized</c>, <c>unsized</c>, and <c>total</c>, <c>min</c>, <c>median</c>, <c>max</c> over the
///     sized ones, <c>null</c> when none is; the median is the lower middle value of an even count), <c>favicon</c> (<c>copied</c>,
///     <c>null</c> with no job) and <c>posts</c> (<c>total</c>, <c>withContact</c>, <c>withPhone</c>, <c>withEmail</c> over linked
///     posts that are not merged, not gone and have a link that is not retired)
///   - <c>totals</c> sums the listed sources (<c>sources</c>, <c>images</c>, <c>bytes</c>, <c>posts</c>); a post linked to several of
///     them counts once
///   - counts only: it never prints an origin URL, a file id, a contact value, post text or any row; an error names the failing
///     argument and nothing else
///   - exit codes: <c>0</c> done, <c>1</c> the store could not be read, <c>2</c> usage
/// </remarks>
public static class AssetsVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "assets";

    private const string Usage = """
        Usage: assets report [--sources <id,id>]
        """;

    private const int ResultLineLimit = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string SourcesSql = """
        SELECT s.id,s.handle,s.family FROM harvest_sources s
        WHERE s.state='active' AND (cardinality(@ids)=0 OR s.id=ANY(@ids))
            AND (EXISTS (SELECT 1 FROM harvest_source_assets a WHERE a.source_id=s.id)
                OR EXISTS (SELECT 1 FROM harvest_post_links l WHERE l.source_id=s.id))
        ORDER BY s.family,s.handle,s.id
        """;

    // A source_id of NULL is the row over every listed source.
    private const string ImagesSql = """
        SELECT a.source_id,
            count(*) FILTER (WHERE a.kind='image'),
            count(*) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL),
            count(*) FILTER (WHERE a.kind='image' AND a.file_id IS NULL AND a.retry_at<=now()),
            count(*) FILTER (WHERE a.kind='image' AND a.file_id IS NULL AND a.retry_at>now()),
            count(a.bytes) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL),
            count(*) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL AND a.bytes IS NULL),
            (sum(a.bytes) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL))::bigint,
            min(a.bytes) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL),
            percentile_disc(0.5) WITHIN GROUP (ORDER BY a.bytes) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL),
            max(a.bytes) FILTER (WHERE a.kind='image' AND a.file_id IS NOT NULL),
            bool_or(a.file_id IS NOT NULL) FILTER (WHERE a.kind='favicon')
        FROM harvest_source_assets a WHERE a.source_id=ANY(@sources)
        GROUP BY GROUPING SETS ((a.source_id),())
        """;

    private const string OutcomesSql = """
        SELECT o.source_id,o.outcome,count(*)
        FROM (SELECT a.source_id,COALESCE(a.outcome,'unrecorded') AS outcome FROM harvest_source_assets a
            WHERE a.kind='image' AND a.source_id=ANY(@sources)) o
        GROUP BY GROUPING SETS ((o.source_id,o.outcome),(o.outcome))
        """;

    private const string PostsSql = """
        WITH live AS (
            SELECT l.source_id,p.id AS post_id,
                CASE WHEN jsonb_typeof(p.base->'contacts')='array' THEN p.base->'contacts' ELSE '[]'::jsonb END AS contacts
            FROM harvest_post_links l JOIN harvest_posts p ON p.container_id=l.container_id AND p.id=l.post_id
            WHERE l.source_id=ANY(@sources) AND l.retired_at IS NULL AND p.merged_into IS NULL AND p.state<>'gone'
        ), flags AS (
            SELECT v.source_id,v.post_id,
                jsonb_array_length(v.contacts)>0 AS any_contact,
                EXISTS (SELECT 1 FROM jsonb_array_elements(v.contacts) c WHERE c->>'kind'='phone') AS phone,
                EXISTS (SELECT 1 FROM jsonb_array_elements(v.contacts) c WHERE c->>'kind'='email') AS email
            FROM live v
        )
        SELECT f.source_id,count(DISTINCT f.post_id),
            count(DISTINCT f.post_id) FILTER (WHERE f.any_contact),
            count(DISTINCT f.post_id) FILTER (WHERE f.phone),
            count(DISTINCT f.post_id) FILTER (WHERE f.email)
        FROM flags f GROUP BY GROUPING SETS ((f.source_id),())
        """;

    /// <summary>Runs the verb against the built internal host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb name.</param>
    /// <param name="output">Standard output for one JSON line.</param>
    /// <param name="error">Standard error for usage or a sanitized diagnostic.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>Zero on success, one when the store could not be read, and two for invalid arguments.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output, TextWriter error,
        CancellationToken ct)
    {
        if (arguments is not ["report", ..] || !VerbArguments.TryOptions(arguments.Skip(1).ToArray(), out var options))
            return await UsageAsync(error);
        if (options.Keys.FirstOrDefault(name => name != "sources") is { } unknown)
            return await InvalidAsync(error, "--" + unknown);
        if (!VerbArguments.TryIds(options, "sources", out var only))
            return await InvalidAsync(error, "--sources");

        try
        {
            var report = await ReadAsync(services.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey), only, ct);
            string line = JsonSerializer.Serialize(report, Json);
            int bytes = Encoding.UTF8.GetByteCount(line);
            if (bytes > ResultLineLimit)
                await error.WriteLineAsync($"The result line is {bytes} bytes; Wheelhouse keeps a last line whole only up to {ResultLineLimit / 1024} KiB. Name fewer sources with --sources.");
            await output.WriteLineAsync(line);
            return 0;
        }
        catch (DbException)
        {
            await error.WriteLineAsync("The store could not be read. Check the SCFD harvest migrations (020-asset-outcomes).");
            return 1;
        }
    }

    private static async Task<Report> ReadAsync(NpgsqlDataSource dataSource, IReadOnlyList<Guid> only, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync(ct);

        DateTime now;
        await using (var clock = new NpgsqlCommand("SELECT now()", connection, transaction))
            now = (DateTime)(await clock.ExecuteScalarAsync(ct))!;

        var listed = new List<Source>();
        await using (var command = new NpgsqlCommand(SourcesSql, connection, transaction))
        {
            command.Parameters.AddWithValue("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid, only.ToArray());
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                listed.Add(new Source(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }
        var ids = listed.Select(source => source.Id).ToArray();

        var images = new Dictionary<Guid, ImageRow>();
        ImageRow allImages = ImageRow.None;
        await foreach (var (id, row) in RowsAsync(connection, transaction, ImagesSql, ids, reader => new ImageRow(
            reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6),
            OptionalLong(reader, 7), OptionalLong(reader, 8), OptionalLong(reader, 9), OptionalLong(reader, 10),
            reader.IsDBNull(11) ? null : reader.GetBoolean(11)), ct))
        {
            if (id is { } sourceId)
                images[sourceId] = row;
            else
                allImages = row;
        }

        var outcomes = new Dictionary<Guid, SortedDictionary<string, long>>();
        var allOutcomes = new SortedDictionary<string, long>(StringComparer.Ordinal);
        await foreach (var (id, row) in RowsAsync(connection, transaction, OutcomesSql, ids, reader => (Outcome: reader.GetString(1), Count: reader.GetInt64(2)), ct))
        {
            if (id is { } sourceId)
            {
                if (!outcomes.TryGetValue(sourceId, out var byOutcome))
                    outcomes[sourceId] = byOutcome = new SortedDictionary<string, long>(StringComparer.Ordinal);
                byOutcome[row.Outcome] = row.Count;
            }
            else
                allOutcomes[row.Outcome] = row.Count;
        }

        var posts = new Dictionary<Guid, PostRow>();
        PostRow allPosts = PostRow.None;
        await foreach (var (id, row) in RowsAsync(connection, transaction, PostsSql, ids,
            reader => new PostRow(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)), ct))
        {
            if (id is { } sourceId)
                posts[sourceId] = row;
            else
                allPosts = row;
        }

        var sources = listed.Select(source =>
        {
            var image = images.GetValueOrDefault(source.Id, ImageRow.None);
            return new SourceReport(source.Id, source.Handle, source.Family,
                new Images(image.Jobs, image.Copied, image.Due, image.Deferred, outcomes.GetValueOrDefault(source.Id) ?? new(StringComparer.Ordinal)),
                image.Sizes(), new Favicon(image.Favicon), posts.GetValueOrDefault(source.Id, PostRow.None).Counts());
        }).ToArray();
        return new Report(new DateTimeOffset(now).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), sources,
            new Totals(sources.Length, new Images(allImages.Jobs, allImages.Copied, allImages.Due, allImages.Deferred, allOutcomes), allImages.Sizes(), allPosts.Counts()));
    }

    /// <summary>Reads a grouped statement over the listed sources: the source id (<c>null</c> for the row over all of them) and its row.</summary>
    private static async IAsyncEnumerable<(Guid? SourceId, T Row)> RowsAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql,
        Guid[] sources, Func<NpgsqlDataReader, T> read, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("sources", NpgsqlDbType.Array | NpgsqlDbType.Uuid, sources);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return (reader.IsDBNull(0) ? null : reader.GetGuid(0), read(reader));
    }

    private static long? OptionalLong(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }

    private static async Task<int> InvalidAsync(TextWriter error, string argument)
    {
        await error.WriteLineAsync($"assets report: {argument} is not valid.");
        return 2;
    }

    private sealed record Source(Guid Id, string Handle, string Family);

    private sealed record ImageRow(long Jobs, long Copied, long Due, long Deferred, long Sized, long Unsized, long? Total, long? Min, long? Median,
        long? Max, bool? Favicon)
    {
        public static ImageRow None { get; } = new(0, 0, 0, 0, 0, 0, null, null, null, null, null);

        public Bytes Sizes() => new(Sized, Unsized, Total, Min, Median, Max);
    }

    private sealed record PostRow(long Total, long WithContact, long WithPhone, long WithEmail)
    {
        public static PostRow None { get; } = new(0, 0, 0, 0);

        public Posts Counts() => new(Total, WithContact, WithPhone, WithEmail);
    }

    private sealed record Report(string GeneratedAt, IReadOnlyList<SourceReport> Sources, Totals Totals);

    private sealed record SourceReport(Guid SourceId, string Handle, string Family, Images Images, Bytes Bytes, Favicon Favicon, Posts Posts);

    private sealed record Totals(int Sources, Images Images, Bytes Bytes, Posts Posts);

    private sealed record Images(long Jobs, long Copied, long Due, long Deferred, SortedDictionary<string, long> ByOutcome);

    private sealed record Bytes(long Sized, long Unsized, long? Total, long? Min, long? Median, long? Max);

    private sealed record Favicon(bool? Copied);

    private sealed record Posts(long Total, long WithContact, long WithPhone, long WithEmail);
}
