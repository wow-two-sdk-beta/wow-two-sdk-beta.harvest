using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Inferences;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Reads a source's processed posts: the tags answer each post carries, its verdict and the extraction answer
/// about the same content, with each answer's cost and latency from the AI module's inference log.</summary>
internal sealed partial class ModuleSourceOverviewBroker
{
    /// <summary>The most inference rows read per post revision when matching an answer to its attempt.</summary>
    private const int InferencesPerSubject = 100;

    /// <inheritdoc />
    public async Task<Result<SourceProcessedPage>> QuerySourceProcessedAsync(Guid sourceId, SourceProcessedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (SourceProcessedQueryRules.Check(sourceId, query, out var after) is { } invalid)
            return Result<SourceProcessedPage>.Fail(invalid);
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
            return Result<SourceProcessedPage>.Fail(RegistrationErrors.SourceNotFound(sourceId));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = PostSql.Command(connection, null, ProcessedSql,
            ("source", source.Id), ("container", source.ContainerId), ("type", query.EntityType ?? ""),
            ("verdict", query.Verdict?.ToString().ToLowerInvariant() ?? ""), ("extracted", (object?)query.Extracted),
            ("after", after.HasValue), ("at", after?.At ?? DateTimeOffset.MinValue), ("post", after?.PostId ?? Guid.Empty),
            ("limit", query.Limit + 1), ("tagsCase", PostTagsConstants.UseCase));
        command.Parameters["extracted"].NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean;
        var rows = new List<ProcessedRow>();
        var total = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                total = reader.GetInt32(33);
                if (!reader.IsDBNull(0))
                    rows.Add(ReadProcessed(reader));
            }

        var page = rows.Take(query.Limit).ToList();
        var attempts = await AttemptsAsync(page, cancellationToken);
        var items = page.Select(row => row.Post with
        {
            Tags = row.Post.Tags with { Answer = Priced(row.Post.Tags.Answer, row.TagsInference, attempts) },
            Extraction = row.Post.Extraction is { } extraction
                ? extraction with { Answer = Priced(extraction.Answer, row.ExtractionInference, attempts) }
                : null,
        }).ToList();
        var next = rows.Count > query.Limit ? SourceProcessedQueryRules.Cursor(sourceId, query, items[^1]) : null;
        return Result<SourceProcessedPage>.Ok(new SourceProcessedPage(items, next, total));
    }

    /// <summary>Selects the source's surviving posts with a tags answer: the done answer about the current title and
    /// text, else the newest done one, else the newest attempt, as the post's own resolve picks; and the extraction of
    /// the tagged type about that answer's content. The total row (all nulls but the count) comes last.</summary>
    private static readonly string ProcessedSql = $"""
        WITH candidates AS (
            SELECT p.id,p.revision,p.first_seen_at,COALESCE(NULLIF(btrim(p.base->>'title'),''),fl.url) AS title,fl.url,
                p.base->'classification' AS classification,{PostTagsContent.SqlHash("p.base")} AS hash
            FROM harvest_posts p
            JOIN LATERAL (
                SELECT l.url FROM harvest_post_links l WHERE l.container_id=p.container_id AND l.post_id=p.id AND l.source_id=@source
                ORDER BY l.first_seen_at,l.id LIMIT 1
            ) fl ON true
            WHERE p.container_id=@container AND p.merged_into IS NULL
        ), processed AS (
            SELECT c.*,t.status AS t_status,t.error AS t_error,t.content_hash=c.hash AS t_current,t.entity_type,
                (SELECT COALESCE(jsonb_agg(jsonb_build_object('key',d->>'node','label',COALESCE(n.label,d->>'node'),
                        'primary',COALESCE((d->>'primary')::boolean,false))
                    ORDER BY COALESCE((d->>'primary')::boolean,false) DESC,x.ord),'[]'::jsonb)
                 FROM jsonb_array_elements(t.domains) WITH ORDINALITY x(d,ord)
                 LEFT JOIN harvest_taxonomy_nodes n ON n.key=d->>'node')::text AS t_domains,
                t.topics,t.about,t.timeframe,t.title_en,t.confidence,t.flags::text AS t_flags,t.model AS t_model,
                t.prompt_version AS t_prompt,t.inference_id AS t_inference,t.created_at AS t_at,t.revision AS t_revision,
                e.use_case,e.status AS e_status,e.error AS e_error,e.role,e.answer::text AS e_answer,e.flags::text AS e_flags,
                e.model AS e_model,e.prompt_version AS e_prompt,e.inference_id AS e_inference,e.updated_at AS e_at,
                e.revision AS e_revision
            FROM candidates c
            JOIN LATERAL (
                SELECT * FROM harvest_post_tags t WHERE t.post_id=c.id
                ORDER BY t.status='done' DESC,t.content_hash=c.hash DESC,t.created_at DESC LIMIT 1
            ) t ON true
            LEFT JOIN LATERAL (
                SELECT * FROM harvest_post_extractions e WHERE e.post_id=c.id AND e.content_hash=t.content_hash
                    AND e.use_case=CASE t.entity_type WHEN 'event' THEN 'extract-event' WHEN 'listing' THEN 'extract-listing' END
                ORDER BY e.status='done' DESC,e.updated_at DESC LIMIT 1
            ) e ON true
            WHERE (@type='' OR t.entity_type=@type)
                AND (@verdict='' OR lower(c.classification->>'verdict')=@verdict)
                AND (@extracted IS NULL OR (e.status IS NOT DISTINCT FROM 'done')=@extracted)
        )
        SELECT * FROM (
            SELECT id,revision,first_seen_at,title,url,classification::text,t_status,t_error,t_current,entity_type,t_domains,
                topics,about,timeframe,title_en,confidence,t_flags,t_model,t_prompt,t_inference,t_at,t_revision,
                use_case,e_status,e_error,role,e_answer,e_flags,e_model,e_prompt,e_inference,e_at,e_revision,
                (SELECT count(*) FROM processed)::int
            FROM processed WHERE NOT @after OR (first_seen_at,id)<(@at,@post)
            ORDER BY first_seen_at DESC,id DESC LIMIT @limit
        ) page
        UNION ALL
        SELECT NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,
            NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,NULL,(SELECT count(*) FROM processed)::int
        """;

    private static ProcessedRow ReadProcessed(NpgsqlDataReader reader)
    {
        string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
        int? Number(int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
        Guid? Id(int index) => reader.IsDBNull(index) ? null : reader.GetGuid(index);

        var tags = new ProcessedTagsModel
        {
            Status = reader.GetString(6),
            Error = Text(7),
            Current = reader.GetBoolean(8),
            EntityType = Text(9),
            Domains = JsonSerializer.Deserialize<ProcessedDomainModel[]>(reader.GetString(10), JsonOptionsConstants.Default) ?? [],
            Topics = reader.GetFieldValue<string[]>(11),
            About = Text(12),
            Timeframe = Text(13),
            TitleEn = Text(14),
            Confidence = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
            Flags = Flags(reader.GetString(16)),
            Answer = Answer(Text(17), Number(18), reader.GetFieldValue<DateTimeOffset>(20)),
        };
        ProcessedExtractionModel? extraction = null;
        if (Text(22) is { } useCase)
        {
            var answer = Text(26);
            extraction = new ProcessedExtractionModel
            {
                UseCase = useCase,
                Status = reader.GetString(23),
                Error = Text(24),
                Role = Text(25),
                Event = answer is not null && useCase == "extract-event" ? EventSummary(answer) : null,
                Listing = answer is not null && useCase == "extract-listing" ? ListingSummary(answer) : null,
                Flags = Flags(reader.GetString(27)),
                Answer = Answer(Text(28), Number(29), reader.GetFieldValue<DateTimeOffset>(31)),
            };
        }
        var post = new SourceProcessedPostModel
        {
            PostId = reader.GetGuid(0),
            Revision = reader.GetInt32(1),
            FirstSeenAt = reader.GetFieldValue<DateTimeOffset>(2),
            Title = reader.GetString(3),
            Url = reader.GetString(4),
            Classification = Text(5) is { } classification
                ? JsonSerializer.Deserialize<PostClassification>(classification, JsonOptionsConstants.Default)
                : null,
            Tags = tags,
            Extraction = extraction,
        };
        return new(post, Attempt(post.PostId, Number(21), Id(19)), Attempt(post.PostId, Number(32), Id(30)));
    }

    /// <summary>Reads each answer's attempt from the inference log, one read per post revision the answers name; empty
    /// when the host composes no inference log.</summary>
    private async Task<IReadOnlyDictionary<Guid, InferenceModel>> AttemptsAsync(IReadOnlyList<ProcessedRow> rows, CancellationToken ct)
    {
        var found = new Dictionary<Guid, InferenceModel>();
        if (services.GetService<IInferencesBroker>() is not { } inferences)
            return found;
        var wanted = rows.SelectMany(row => new[] { row.TagsInference, row.ExtractionInference }).OfType<AttemptKey>()
            .GroupBy(key => key.Subject);
        foreach (var subject in wanted)
        {
            var ids = subject.Select(key => key.InferenceId).ToHashSet();
            if (await inferences.ListAsync(new InferenceQuery { Subject = subject.Key, Limit = InferencesPerSubject }, ct)
                is Result<IReadOnlyList<InferenceModel>>.Success { Value: var attempts })
                foreach (var attempt in attempts.Where(attempt => ids.Contains(attempt.Id)))
                    found[attempt.Id] = attempt;
        }
        return found;
    }

    private static AttemptKey? Attempt(Guid postId, int? revision, Guid? inferenceId) =>
        revision is { } number && inferenceId is { } id ? new($"{PostTagsConstants.SubjectPrefix}{postId:D}@{number}", id) : null;

    private static ProcessedAnswerModel Priced(ProcessedAnswerModel answer, AttemptKey? key, IReadOnlyDictionary<Guid, InferenceModel> attempts) =>
        key is not null && attempts.TryGetValue(key.InferenceId, out var attempt)
            ? answer with { CostMicroUsd = attempt.CostMicroUsd, LatencyMs = attempt.LatencyMs }
            : answer;

    /// <summary>Splits the stored <c>{provider}/{model}</c> route into its provider and model.</summary>
    private static ProcessedAnswerModel Answer(string? route, int? promptVersion, DateTimeOffset at)
    {
        var slash = route?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        return new ProcessedAnswerModel
        {
            Provider = slash > 0 ? route![..slash] : null,
            Model = slash > 0 ? route![(slash + 1)..] : route,
            PromptVersion = promptVersion,
            AnsweredAt = at,
        };
    }

    private static IReadOnlyList<ProcessedFlagModel> Flags(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.EnumerateArray().Where(flag => flag.ValueKind == JsonValueKind.Object)
            .Select(flag => new ProcessedFlagModel(Str(flag, "field") ?? "", Str(flag, "reason") ?? "", Str(flag, "proposed")))];
    }

    private static ProcessedEventSummary EventSummary(string json)
    {
        using var document = JsonDocument.Parse(json);
        var answer = document.RootElement;
        var name = Child(answer, "name");
        var place = Child(answer, "place");
        return new ProcessedEventSummary
        {
            Name = Str(name, "en"),
            OriginalName = Str(name, "original"),
            EditionLabel = Str(answer, "editionLabel"),
            Occurrences = [.. Items(answer, "occurrences").Select(run =>
                new ProcessedOccurrenceModel(Str(run, "starts"), Str(run, "ends"), Str(run, "precision"), Str(run, "certainty")))],
            Venue = Str(place, "venue"),
            City = Str(place, "city"),
            Country = Str(place, "country"),
            Format = Str(answer, "format"),
            Access = Str(answer, "access"),
            Prices = [.. Items(answer, "prices").Select(price => new ProcessedPriceModel(Dec(price, "amount"), Str(price, "currency"), Str(price, "label")))],
            Status = Str(answer, "status"),
        };
    }

    private static ProcessedListingSummary ListingSummary(string json)
    {
        using var document = JsonDocument.Parse(json);
        var answer = document.RootElement;
        var price = Child(answer, "price");
        var item = Child(answer, "item");
        var location = Child(answer, "location");
        return new ProcessedListingSummary
        {
            OfferType = Str(answer, "offerType"),
            Price = price.ValueKind == JsonValueKind.Object
                ? new ProcessedPriceModel(Dec(price, "amount"), Str(price, "currency"), Str(price, "period"))
                : null,
            Rooms = Dec(item, "rooms") is { } rooms ? (int)rooms : null,
            AreaM2 = Dec(item, "areaM2"),
            District = Str(location, "district"),
            City = Str(location, "city"),
        };
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        Child(parent, name) is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Str(JsonElement parent, string name) =>
        Child(parent, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static decimal? Dec(JsonElement parent, string name) =>
        Child(parent, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDecimal(out var number) ? number
        : Child(parent, name) is { ValueKind: JsonValueKind.String } text
          && decimal.TryParse(text.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    /// <summary>Names one answer's attempt: the inference log's subject for the post revision it answered, and the
    /// attempt's id.</summary>
    private sealed record AttemptKey(string Subject, Guid InferenceId);

    private sealed record ProcessedRow(SourceProcessedPostModel Post, AttemptKey? TagsInference, AttemptKey? ExtractionInference);
}
