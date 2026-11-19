using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Claims and processes saved readings without consulting the marketplace.</summary>
internal static class ProcessingStore
{
    internal const string Step = "process";
    internal static IReadOnlyList<string> OperationKeys(ICaptureProcessor? processor) =>
        ["load-capture", "deserialize-capture",
            .. (processor?.OperationKeys is { Count: > 0 } keys ? keys : ["process-capture"]),
            "save-result"];

    internal static async Task SeedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, FrozenRun frozen,
        Guid run, DateTimeOffset now, CancellationToken ct)
    {
        var stage = frozen.Stage!;
        var inputs = new List<(Guid Capture, Guid Link, string Url)>();
        string processor = CaptureProcessors.ResultKey(frozen.Stage);
        await using (var command = EngineSql.Command(connection, transaction, """
            SELECT reading.capture_id,link.id,link.url
            FROM harvest_post_links link
            JOIN harvest_link_readings reading ON reading.link_id=link.id
            JOIN harvest_posts post ON post.id=link.post_id
            JOIN harvest_captures capture ON capture.captured_at=reading.captured_at AND capture.id=reading.capture_id
            LEFT JOIN harvest_runs origin ON origin.id=capture.run_id
            LEFT JOIN harvest_processing_results result ON result.capture_id=reading.capture_id AND result.processor=@processor
            WHERE link.container_id=@container AND link.source_id=@source AND link.retired_at IS NULL AND post.purged_at IS NULL
              AND (@whole OR origin.pipeline_id=@pipeline OR link.url=@url OR EXISTS (
                SELECT 1 FROM harvest_discoveries d JOIN harvest_discovery_scopes s ON s.discovery_id=d.id
                WHERE d.link_id=link.id AND s.pipeline_id=@pipeline))
              AND (@title::text IS NULL OR strpos(lower(COALESCE(reading.title,'')),lower(@title::text))>0)
              AND (@since::timestamptz IS NULL OR post.published_at>=@since)
              AND (result.capture_id IS NULL OR (result.result IS NULL AND result.next_ready_at<=@now AND NOT EXISTS (
                SELECT 1 FROM harvest_runs r WHERE r.id=result.run_id AND r.status IN ('queued','running','paused'))))
            ORDER BY reading.captured_at,reading.capture_id LIMIT @batch
            """, ("processor", processor), ("container", frozen.Pipeline.ContainerId), ("source", frozen.Source.Id),
            ("whole", frozen.Pipeline.ScopeKey == "*"), ("pipeline", frozen.Pipeline.Id), ("url", frozen.Pipeline.Scope.Url),
            ("title", string.IsNullOrWhiteSpace(stage.TitleContains) ? null : stage.TitleContains.Trim()),
            ("since", stage.PublishedSinceUtc?.ToUniversalTime()), ("now", now), ("batch", stage.BatchSize)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                inputs.Add((reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2)));
        }
        foreach (var input in inputs)
        {
            var target = new StepTarget
            {
                StepKey = Step, Target = input.Capture.ToString("D"),
                Payload = JsonSerializer.SerializeToElement(new { captureId = input.Capture, linkId = input.Link, url = input.Url },
                    JsonOptionsConstants.Default),
            };
            if (await RunPlanner.EnqueueAsync(connection, transaction, frozen, run, target, now, ct) != RunPlanner.EnqueueResult.Enqueued)
                continue;
            await EngineSql.ExecuteAsync(connection, transaction, """
                INSERT INTO harvest_processing_results(capture_id,processor,container_id,link_id,run_id,next_ready_at)
                VALUES(@capture,@processor,@container,@link,@run,@now)
                ON CONFLICT(capture_id,processor) DO UPDATE SET run_id=excluded.run_id WHERE harvest_processing_results.result IS NULL
                """, ct, ("capture", input.Capture), ("processor", processor), ("container", frozen.Pipeline.ContainerId),
                ("link", input.Link), ("run", run), ("now", now));
        }
    }

    internal static async Task<CaptureProcessResult> ProcessAsync(NpgsqlDataSource database, IServiceProvider services,
        FrozenRun frozen, StepItem item, StepTrace trace, CancellationToken ct)
    {
        var processor = CaptureProcessors.Resolve(services, frozen.Stage);
        if (processor is null)
            return new(new StepResult { Outcome = FetchOutcome.CapabilityUnavailable, Message = "The capture processor is unavailable." });
        // Source opt-out applies to already queued work too; the frozen run is provenance, not current permission.
        var registry = services.GetRequiredService<IRegistrationBroker>();
        if (await registry.GetSourceAsync(frozen.Source.Id, ct) is not Result<SourceModel>.Success { Value: var source })
            return new(new StepResult { Outcome = FetchOutcome.CapabilityUnavailable, Message = "The source is unavailable." });
        // A manual source keeps a run started by hand, but no longer a scheduled one queued before the switch.
        bool automatic = source.ModelProcessing == ModelProcessingMode.Manual && await ScheduledAsync(database, item.RunId, ct);
        if (CaptureProcessors.Preflight(processor, source, frozen.Stage!, automatic) is { } error)
            return new(new StepResult { Outcome = FetchOutcome.CapabilityUnavailable, Message = error.Message });

        var (capture, link) = InputIds(item);
        var reading = await trace.RunAsync("load-capture", () => LoadReadingAsync(database, frozen, item, capture, link, ct),
            value => new(value is null ? "skipped" : "completed", Input: 1, Output: value is null ? 0 : 1,
                Message: value is null ? "The source has a newer capture." : null));
        if (reading is null)
            return new(new StepResult { Outcome = FetchOutcome.Gone, Message = "The source has a newer capture." });

        var saved = reading.Value;
        var envelope = await trace.RunAsync("deserialize-capture", () =>
            Task.FromResult(JsonSerializer.Deserialize<PostEnvelope>(saved.Fields, JsonOptionsConstants.Default) ?? throw new JsonException()),
            _ => new(Input: 1, Output: 1));
        var input = new CaptureProcessInput(item.ContainerId, source, saved.PostId, link, capture,
            envelope.Title, envelope.Text, envelope.Language, envelope.Extras, frozen.Stage?.Model, trace, frozen.Stage?.PromptVersion);

        return processor.OperationKeys.Count > 0
            ? await processor.ProcessAsync(input, ct)
            : await trace.RunAsync("process-capture", () => processor.ProcessAsync(input, ct),
                result => new(Input: 1, Output: result.Output is null ? 0 : 1));
    }

    private static async Task<bool> ScheduledAsync(NpgsqlDataSource database, Guid runId, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = EngineSql.Command(connection, null, "SELECT trigger='schedule' FROM harvest_runs WHERE id=@run", ("run", runId));
        return await command.ExecuteScalarAsync(ct) is true;
    }

    private static async Task<(Guid PostId, string Fields)?> LoadReadingAsync(NpgsqlDataSource database, FrozenRun frozen,
        StepItem item, Guid capture, Guid link, CancellationToken ct)
    {
        await using var connection = await database.OpenConnectionAsync(ct);
        await using var command = EngineSql.Command(connection, null, """
            SELECT link.post_id,reading.fields::text FROM harvest_link_readings reading
            JOIN harvest_post_links link ON link.container_id=reading.container_id AND link.id=reading.link_id
            JOIN harvest_posts post ON post.container_id=link.container_id AND post.id=link.post_id AND post.purged_at IS NULL
            JOIN harvest_processing_results result ON result.capture_id=reading.capture_id AND result.link_id=reading.link_id
            WHERE reading.container_id=@container AND reading.link_id=@link AND reading.capture_id=@capture
              AND link.retired_at IS NULL AND result.processor=@processor AND result.run_id=@run AND result.result IS NULL
            """, ("container", item.ContainerId), ("link", link), ("capture", capture),
            ("processor", CaptureProcessors.ResultKey(frozen.Stage)), ("run", item.RunId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetGuid(0), reader.GetString(1)) : null;
    }

    internal static async Task<int> CompleteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, FrozenRun frozen,
        StepItem item, JsonElement? output, DateTimeOffset now, CancellationToken ct, bool retryable)
    {
        var (capture, link) = InputIds(item);
        // Recheck after the model returns; a purge or retired link must not regain derived text.
        await using (var visibility = EngineSql.Command(connection, transaction, """
            SELECT 1 FROM harvest_post_links link
            JOIN harvest_posts post ON post.container_id=link.container_id AND post.id=link.post_id
            WHERE link.container_id=@container AND link.id=@link AND link.retired_at IS NULL AND post.purged_at IS NULL
            FOR SHARE OF link,post
            """, ("container", item.ContainerId), ("link", link)))
            if (await visibility.ExecuteScalarAsync(ct) is null)
                return 0;
        return await EngineSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_processing_results SET result=@result::jsonb,
                processed_at=CASE WHEN @result::jsonb IS NULL THEN NULL ELSE @now END,
                next_ready_at=CASE WHEN @retryable THEN @retry ELSE 'infinity'::timestamptz END
            WHERE capture_id=@capture AND processor=@processor AND run_id=@run AND result IS NULL
            """, ct, ("result", output?.GetRawText()), ("now", now), ("retry", now.AddMinutes(5)), ("retryable", retryable),
            ("capture", capture), ("processor", CaptureProcessors.ResultKey(frozen.Stage)), ("run", item.RunId));
    }

    private static (Guid Capture, Guid Link) InputIds(StepItem item)
    {
        var payload = item.Payload!.Value;
        return (payload.GetProperty("captureId").GetGuid(), payload.GetProperty("linkId").GetGuid());
    }
}
