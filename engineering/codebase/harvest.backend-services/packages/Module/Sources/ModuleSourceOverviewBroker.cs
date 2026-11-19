using System.Data;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Reads the console's Sources area over the registry, run, post and delivery tables.</summary>
/// <remarks>Sources and pipelines come through <see cref="IRegistrationBroker"/>; the rest is one query per table
/// for the whole container, so an overview costs the same few round trips however many sources it holds.</remarks>
internal sealed partial class ModuleSourceOverviewBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    IRegistrationBroker registry,
    TimeProvider time,
    IEnumerable<ISourceAdapter> adapters,
    IServiceProvider services) : ISourceOverviewBroker
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SourceOverviewModel>>> ListOverviewAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        if (await registry.GetContainerAsync(containerId, cancellationToken) is not Result<ContainerModel>.Success)
            return Result<IReadOnlyList<SourceOverviewModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        if (await registry.ListSourcesAsync(containerId, cancellationToken) is not Result<IReadOnlyList<SourceModel>>.Success { Value: var sources }
            || await registry.ListPipelinesAsync(containerId, cancellationToken) is not Result<IReadOnlyList<PipelineModel>>.Success { Value: var pipelines })
            return Result<IReadOnlyList<SourceOverviewModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        var now = time.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var lastRuns = await LastRunsAsync(connection, containerId, cancellationToken);
        var lastHarvests = await LastHarvestsAsync(connection, containerId, cancellationToken);
        var active = await ActiveSourcesAsync(connection, containerId, cancellationToken);
        var daily = await DailyAsync(connection, containerId, now, cancellationToken);
        var (sourcePosts, sectionPosts) = await PostCountsAsync(connection, containerId, cancellationToken);
        var subscribers = await SubscribersAsync(connection, containerId, cancellationToken);
        var favicons = await FaviconsAsync(connection, containerId, cancellationToken);
        var addresses = new Dictionary<Guid, SourceAddress>();
        foreach (var source in sources)
        {
            var descriptor = adapters.LastOrDefault(adapter => adapter.Serves(source))?.Describe(source);
            if (SourceAddress.Resolve(source, descriptor) is not { } address)
                return Result<IReadOnlyList<SourceOverviewModel>>.Fail(AppErrorFactory.Validation("The adapter's source address is invalid."));
            addresses.Add(source.Id, address);
        }
        var hosts = await SourceHealthSql.ReadHostsAsync(connection, [.. addresses.Values.Select(address => address.HostKey).Distinct()], cancellationToken);
        var refusals = await SourceHealthSql.ReadRefusalsAsync(connection, [.. sources.Select(source => source.Id)], cancellationToken);

        var bySource = pipelines.ToLookup(pipeline => pipeline.SourceId);
        var overviews = new List<SourceOverviewModel>(sources.Count);
        foreach (var source in sources)
        {
            var readable = SourceHarvestSelection.CanRead(source);
            var sections = bySource[source.Id].Select(pipeline => Section(pipeline, source, lastRuns, sectionPosts, subscribers)).ToList();
            string hostKey = addresses[source.Id].HostKey;
            var health = SourceOverviewRules.Health(hostKey, hosts.GetValueOrDefault(hostKey), refusals.GetValueOrDefault(source.Id), now);
            var lastHarvest = lastHarvests.GetValueOrDefault(source.Id);
            overviews.Add(new SourceOverviewModel
            {
                SourceId = source.Id,
                Name = source.Name ?? source.Handle,
                Family = source.Family,
                Handle = source.Handle,
                FaviconUrl = favicons.TryGetValue(source.Id, out Guid icon) ? SourceRouteConstants.AssetUrl(source.Id, icon) : null,
                GroupId = source.GroupId,
                State = readable ? SourceOverviewRules.State(active.Contains(source.Id), health, lastHarvest, sections)
                    : source.State == SourceState.Paused ? SourceHarvestState.Idle : SourceHarvestState.NeedsAttention,
                SourceState = source.State,
                Permission = source.Permission,
                LastHarvest = lastHarvest,
                NextHarvestAt = sections.Where(section => section.Harvesting && section.NextDueAt is not null).Min(section => section.NextDueAt),
                PostCount = sourcePosts.GetValueOrDefault(source.Id),
                NewPostsDaily = SourceOverviewRules.DailySeries(daily.GetValueOrDefault(source.Id) ?? [], now),
                Sections = sections,
                Health = health,
            });
        }

        return Result<IReadOnlyList<SourceOverviewModel>>.Ok([.. overviews
            .OrderBy(overview => overview.Name, StringComparer.OrdinalIgnoreCase).ThenBy(overview => overview.SourceId)]);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SourcePostModel>>> ListSourcePostsAsync(Guid sourceId, int limit = SourceConstants.DefaultPostsLimit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > SourceConstants.MaxPostsLimit)
            return Result<IReadOnlyList<SourcePostModel>>.Fail(
                RegistrationErrors.Invalid($"The limit must be between 1 and {SourceConstants.MaxPostsLimit}."));
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
            return Result<IReadOnlyList<SourcePostModel>>.Fail(RegistrationErrors.SourceNotFound(sourceId));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = ReadPosts(connection, null, source, limit, [], null, "", DateTimeOffset.MaxValue, media: true, listing: true);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var posts = new List<SourcePostModel>();
        while (await reader.ReadAsync(cancellationToken))
            posts.Add(ReadPost(reader));
        return Result<IReadOnlyList<SourcePostModel>>.Ok(posts);
    }

    /// <inheritdoc />
    public async Task<Result<SourcePostPage>> QuerySourcePostsAsync(Guid sourceId, SourcePostQuery query, CancellationToken cancellationToken = default)
    {
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source }
            || await registry.ListPipelinesAsync(source.ContainerId, cancellationToken) is not Result<IReadOnlyList<PipelineModel>>.Success { Value: var pipelines })
            return Result<SourcePostPage>.Fail(RegistrationErrors.SourceNotFound(sourceId));

        var adapter = adapters.LastOrDefault(candidate => candidate.Serves(source)) as ISourceConfigurationProvider;
        var started = SourcePostQuerySelection.Start(source, query, time.GetUtcNow(), pipelines, adapter);
        if (started is Result<SourcePostQuerySelection>.Failure { Error: var error })
            return Result<SourcePostPage>.Fail(error);
        var selection = ((Result<SourcePostQuerySelection>.Success)started).Value;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var scan = ReadPosts(connection, transaction, source, 0, [], query.PostId, query.Search?.Trim() ?? "",
            selection.AsOf, media: false, listing: selection.UsesListingRooms))
        await using (var reader = await scan.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                selection.Add(ReadPost(reader));

        var page = selection.Complete();
        if (page.Items.Count == 0)
            return Result<SourcePostPage>.Ok(page);

        var items = new List<SourcePostModel>(page.Items.Count);
        await using (var hydrate = ReadPosts(connection, transaction, source, page.Items.Count,
            [.. page.Items.Select(post => post.PostId)], null, "", selection.AsOf, media: true, listing: true))
        await using (var reader = await hydrate.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var post = ReadPost(reader);
                items.Add(post with
                {
                    PublisherCategory = selection.CategoryLabel(post),
                    EventTiming = adapter?.ReadPostFilter(SourcePostFilterKeys.EventTiming, post, selection.AsOf),
                });
            }
        }
        return Result<SourcePostPage>.Ok(page with { Items = items });
    }

    private static NpgsqlCommand ReadPosts(NpgsqlConnection connection, NpgsqlTransaction? transaction, SourceModel source,
        int limit, Guid[] ids, Guid? exact, string search, DateTimeOffset asOf, bool media, bool listing) =>
        PostSql.Command(connection, transaction, """
            SELECT p.id,p.base->>'title',p.kind,p.state,p.revision,p.first_seen_at,p.last_changed_at,fc.pipeline_id,
                (SELECT d->>'value' FROM jsonb_array_elements(CASE WHEN jsonb_typeof(p.base->'dates')='array'
                    THEN p.base->'dates' ELSE '[]'::jsonb END) d WHERE d->>'role'='start' LIMIT 1),
                fl.url,
                (SELECT d->>'value' FROM jsonb_array_elements(CASE WHEN jsonb_typeof(p.base->'dates')='array'
                    THEN p.base->'dates' ELSE '[]'::jsonb END) d WHERE d->>'role'='end' LIMIT 1),
                p.base->'postedAt'->>'value',
                CASE WHEN @media THEN (SELECT ARRAY(SELECT CASE WHEN a.file_id IS NULL THEN m.value->>'url'
                        ELSE @asset_prefix || a.id::text END FROM jsonb_array_elements(CASE WHEN jsonb_typeof(p.base->'media')='array'
                        THEN p.base->'media' ELSE '[]'::jsonb END) WITH ORDINALITY m(value,position)
                    LEFT JOIN harvest_source_assets a ON a.source_id=@source AND a.kind='image' AND a.origin_url=m.value->>'url'
                    WHERE m.value->>'kind'='image' AND m.value->>'url' IS NOT NULL ORDER BY m.position LIMIT @images)) ELSE ARRAY[]::text[] END,
                coalesce(nullif(fl.locale, ''), nullif(p.base->'language'->>'code', 'und')),
                CASE WHEN @listing AND p.kind='listing' THEN COALESCE(reading.fields,p.base)::text END
            FROM harvest_posts p
            JOIN LATERAL (
                SELECT l.id,l.url,l.locale FROM harvest_post_links l WHERE l.container_id=p.container_id AND l.post_id=p.id AND l.source_id=@source
                ORDER BY l.first_seen_at,l.id LIMIT 1
            ) fl ON true
            LEFT JOIN harvest_link_readings reading ON @listing AND p.kind='listing' AND reading.link_id=fl.id AND reading.container_id=p.container_id
            LEFT JOIN LATERAL (
                SELECT r.pipeline_id FROM harvest_post_links l
                JOIN harvest_captures c ON c.container_id=l.container_id AND c.link_id=l.id
                LEFT JOIN harvest_runs r ON r.id=c.run_id
                WHERE l.container_id=p.container_id AND l.post_id=p.id AND l.source_id=@source
                ORDER BY c.captured_at,c.id LIMIT 1
            ) fc ON true
            WHERE p.container_id=@container AND p.merged_into IS NULL AND p.first_seen_at<=@as_of
                AND (NOT @exact OR p.id=@post_id)
                AND (cardinality(@ids)=0 OR p.id=ANY(@ids))
                AND (@search='' OR strpos(lower(COALESCE(NULLIF(btrim(p.base->>'title'), ''),fl.url)),lower(@search))>0)
            ORDER BY p.first_seen_at DESC,p.id DESC LIMIT NULLIF(@limit,0)
            """, ("source", source.Id), ("container", source.ContainerId), ("limit", limit), ("images", SourceConstants.MaxPostImages),
            ("asset_prefix", $"/api/sources/{source.Id:D}/{SourceRouteConstants.Assets}/"), ("as_of", asOf),
            ("ids", ids), ("exact", exact.HasValue), ("post_id", exact ?? Guid.Empty), ("search", search), ("media", media), ("listing", listing));

    private static SourcePostModel ReadPost(NpgsqlDataReader reader)
    {
        var url = reader.GetString(9);
        var images = reader.GetFieldValue<string[]>(12);
        return new SourcePostModel
        {
            PostId = reader.GetGuid(0),
            Title = reader.IsDBNull(1) || string.IsNullOrWhiteSpace(reader.GetString(1)) ? url : reader.GetString(1),
            Kind = Enum.TryParse<PostKind>(reader.GetString(2), true, out var kind) ? kind : PostKind.Other,
            State = Enum.Parse<PostState>(reader.GetString(3), true),
            Revision = reader.GetInt32(4),
            FirstSeenAt = reader.GetFieldValue<DateTimeOffset>(5),
            LastChangedAt = reader.GetFieldValue<DateTimeOffset>(6),
            PipelineId = reader.IsDBNull(7) ? null : reader.GetGuid(7),
            StartsAt = reader.IsDBNull(8) ? null : reader.GetString(8),
            Url = url,
            EndsAt = reader.IsDBNull(10) ? null : reader.GetString(10),
            PostedAt = reader.IsDBNull(11) ? null : reader.GetString(11),
            ImageUrl = images.FirstOrDefault(),
            ImageUrls = images,
            Language = reader.IsDBNull(13) ? null : reader.GetString(13),
            Listing = reader.IsDBNull(14) ? null : ListingSummary(reader.GetString(14), url),
        };
    }

    private static SourceListingSummary? ListingSummary(string envelope, string url)
    {
        var source = JsonSerializer.Deserialize<PostEnvelope>(envelope, JsonOptionsConstants.Default);
        if (source is null || ListingSchemaMapper.ReadOrProject(source, url) is not { } listing) return null;
        var offer = listing.Offers.FirstOrDefault();
        return new(listing.Property.Kind, listing.Unit.Rooms, listing.Unit.TotalAreaM2, listing.Unit.Floor,
            listing.Unit.BuildingFloors, listing.Place?.City, listing.Place?.District, offer?.Transaction, offer?.Period, offer?.Price);
    }

    private static async Task<Dictionary<Guid, Guid>> FaviconsAsync(NpgsqlConnection connection, Guid containerId, CancellationToken ct)
    {
        var icons = new Dictionary<Guid, Guid>();
        await using var command = PostSql.Command(connection, null, """
            SELECT a.source_id,a.id FROM harvest_source_assets a JOIN harvest_sources s ON s.id=a.source_id
            WHERE s.container_id=@container AND a.kind='favicon' AND a.file_id IS NOT NULL
            """, ("container", containerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            icons.Add(reader.GetGuid(0), reader.GetGuid(1));
        return icons;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PostRevisionModel>>> ListPostRevisionsAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (await PostSql.ScalarAsync(connection, null, "SELECT 1 FROM harvest_posts WHERE container_id=@container AND id=@post",
                cancellationToken, ("container", containerId), ("post", postId)) is null)
            return Result<IReadOnlyList<PostRevisionModel>>.Fail(AppErrorFactory.NotFound("Post not found in the container."));

        await using var command = PostSql.Command(connection, null,
            "SELECT revision,at,changed FROM harvest_post_revisions WHERE container_id=@container AND post_id=@post ORDER BY revision DESC",
            ("container", containerId), ("post", postId));
        var revisions = new List<PostRevisionModel>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            revisions.Add(new PostRevisionModel
            {
                Revision = reader.GetInt32(0),
                At = reader.GetFieldValue<DateTimeOffset>(1),
                Changed = reader.GetFieldValue<string[]>(2),
            });
        return Result<IReadOnlyList<PostRevisionModel>>.Ok(revisions);
    }

    private static SourceSectionModel Section(PipelineModel pipeline, SourceModel source, IReadOnlyDictionary<Guid, RunRow> lastRuns,
        IReadOnlyDictionary<Guid, int> posts, IReadOnlyDictionary<Guid, int> subscribers)
    {
        var readable = SourceHarvestSelection.CanRead(source);
        var harvesting = readable && SourceOverviewRules.IsHarvesting(pipeline);
        var next = harvesting ? SourceHarvestSelection.Automatic(pipeline).Where(schedule => schedule.NextDueAtUtc is not null)
            .OrderBy(schedule => schedule.NextDueAtUtc).FirstOrDefault() : null;
        var run = lastRuns.GetValueOrDefault(pipeline.Id);
        return new SourceSectionModel
        {
            PipelineId = pipeline.Id,
            Name = pipeline.Name,
            Url = string.IsNullOrWhiteSpace(pipeline.Scope.Url) ? null : pipeline.Scope.Url,
            Harvesting = harvesting,
            Paused = pipeline.State == PipelineState.Paused,
            HarvestKind = readable ? SourceHarvestSelection.ManualKind(pipeline) : null,
            NextWorkKind = next?.Kind,
            CaptureEvery = SourceOverviewRules.Enabled(pipeline, WorkKind.Capture)?.Every,
            CheckEvery = SourceOverviewRules.Enabled(pipeline, WorkKind.Check)?.Every,
            MaxPages = pipeline.Limits.MaxPages,
            LastRun = run is null ? null : new SourceSectionRunModel
            {
                RunId = run.Id, Kind = run.Kind, At = run.At, Status = run.Status, Reason = run.Reason,
                NewPosts = run.Captured, Found = run.Discovered,
            },
            NextDueAt = next?.NextDueAtUtc,
            Posts = posts.GetValueOrDefault(pipeline.Id),
            Subscribers = subscribers.GetValueOrDefault(pipeline.Id),
        };
    }

    private const string RunColumns = "r.id,r.pipeline_id,r.kind,r.status,COALESCE(r.finished_at,r.queued_at),r.completion_reason,coalesce(r.new_posts,r.captured),r.discovered";

    private static RunRow ReadRun(NpgsqlDataReader reader, int offset) => new(
        reader.GetGuid(offset), reader.GetGuid(offset + 1), SnakeCaseEnum<WorkKind>.Parse(reader.GetString(offset + 2)),
        SnakeCaseEnum<RunStatus>.Parse(reader.GetString(offset + 3)), reader.GetFieldValue<DateTimeOffset>(offset + 4),
        reader.IsDBNull(offset + 5) ? null : reader.GetString(offset + 5), reader.GetInt32(offset + 6), reader.GetInt32(offset + 7));

    /// <summary>Reads each pipeline's newest run of any kind.</summary>
    private static async Task<Dictionary<Guid, RunRow>> LastRunsAsync(NpgsqlConnection connection, Guid containerId, CancellationToken ct)
    {
        var runs = new Dictionary<Guid, RunRow>();
        await using var command = PostSql.Command(connection, null,
            $"SELECT DISTINCT ON (r.pipeline_id) {RunColumns} FROM harvest_runs r WHERE r.container_id=@container ORDER BY r.pipeline_id,r.queued_at DESC,r.id DESC",
            ("container", containerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var run = ReadRun(reader, 0);
            runs[run.PipelineId] = run;
        }

        return runs;
    }

    /// <summary>Reads each source's newest collection or processing run across its pipelines.</summary>
    private static async Task<Dictionary<Guid, SourceHarvestModel>> LastHarvestsAsync(NpgsqlConnection connection, Guid containerId, CancellationToken ct)
    {
        var harvests = new Dictionary<Guid, SourceHarvestModel>();
        await using var command = PostSql.Command(connection, null, $"""
            SELECT DISTINCT ON (p.source_id) p.source_id,{RunColumns}
            FROM harvest_runs r JOIN harvest_pipelines p ON p.id=r.pipeline_id
            WHERE r.container_id=@container AND r.kind IN ('capture','discover','harvest','process')
            ORDER BY p.source_id,r.queued_at DESC,r.id DESC
            """, ("container", containerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var run = ReadRun(reader, 1);
            harvests[reader.GetGuid(0)] = new SourceHarvestModel
            {
                RunId = run.Id, PipelineId = run.PipelineId, Kind = run.Kind, At = run.At, Status = run.Status, Reason = run.Reason,
                NewPosts = run.Captured, Found = run.Discovered,
            };
        }

        return harvests;
    }

    /// <summary>Reads the sources with a queued or running run.</summary>
    private static async Task<HashSet<Guid>> ActiveSourcesAsync(NpgsqlConnection connection, Guid containerId, CancellationToken ct)
    {
        var sources = new HashSet<Guid>();
        await using var command = PostSql.Command(connection, null, """
            SELECT DISTINCT p.source_id FROM harvest_runs r JOIN harvest_pipelines p ON p.id=r.pipeline_id
            WHERE r.container_id=@container AND r.status IN ('queued','running')
            """, ("container", containerId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            sources.Add(reader.GetGuid(0));
        return sources;
    }

    /// <summary>Counts each source's links first seen per UTC day of the overview's window.</summary>
    private static async Task<Dictionary<Guid, Dictionary<DateOnly, int>>> DailyAsync(NpgsqlConnection connection, Guid containerId,
        DateTimeOffset now, CancellationToken ct)
    {
        var start = SourceOverviewRules.SeriesStart(now);
        var counts = new Dictionary<Guid, Dictionary<DateOnly, int>>();
        await using var command = PostSql.Command(connection, null, """
            SELECT source_id,CAST(first_seen_at AT TIME ZONE 'UTC' AS date),count(*)::int FROM harvest_post_links
            WHERE container_id=@container AND first_seen_at>=@from AND first_seen_at<@to
            GROUP BY 1,2
            """, ("container", containerId), ("from", start), ("to", start.AddDays(SourceConstants.OverviewDays)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var source = reader.GetGuid(0);
            if (!counts.TryGetValue(source, out var days))
                counts[source] = days = [];
            days[reader.GetFieldValue<DateOnly>(1)] = reader.GetInt32(2);
        }

        return counts;
    }

    /// <summary>Counts each source's posts and attributes their first captures to harvest sections.</summary>
    private static async Task<(Dictionary<Guid, int> Sources, Dictionary<Guid, int> Sections)> PostCountsAsync(
        NpgsqlConnection connection, Guid containerId, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, null, """
            SELECT f.source_id,r.pipeline_id,count(*)::int FROM (
                SELECT DISTINCT ON (l.post_id,l.source_id) l.post_id,l.source_id,c.run_id
                FROM harvest_post_links l LEFT JOIN harvest_captures c ON c.container_id=l.container_id AND c.link_id=l.id
                WHERE l.container_id=@container
                ORDER BY l.post_id,l.source_id,c.captured_at,c.id
            ) f
            LEFT JOIN harvest_runs r ON r.id=f.run_id
            JOIN harvest_posts p ON p.container_id=@container AND p.id=f.post_id AND p.merged_into IS NULL
            GROUP BY GROUPING SETS ((f.source_id),(r.pipeline_id))
            """, ("container", containerId));
        var sources = new Dictionary<Guid, int>();
        var sections = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0)) sources.Add(reader.GetGuid(0), reader.GetInt32(2));
            if (!reader.IsDBNull(1)) sections.Add(reader.GetGuid(1), reader.GetInt32(2));
        }
        return (sources, sections);
    }

    /// <summary>Counts each pipeline's active subscriptions.</summary>
    private static async Task<Dictionary<Guid, int>> SubscribersAsync(NpgsqlConnection connection, Guid containerId, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, null, """
            SELECT pipeline_id,count(*)::int FROM harvest_subscriptions
            WHERE container_id=@container AND state='active' AND pipeline_id IS NOT NULL GROUP BY pipeline_id
            """, ("container", containerId));
        return await CountsAsync(command, ct);
    }

    private static async Task<Dictionary<Guid, int>> CountsAsync(NpgsqlCommand command, CancellationToken ct)
    {
        var counts = new Dictionary<Guid, int>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            counts[reader.GetGuid(0)] = reader.GetInt32(1);
        return counts;
    }

    private sealed record RunRow(Guid Id, Guid PipelineId, WorkKind Kind, RunStatus Status, DateTimeOffset At, string? Reason, int Captured, int Discovered);
}
