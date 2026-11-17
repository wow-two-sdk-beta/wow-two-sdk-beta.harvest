using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Reads processor evidence through the owning post and container.</summary>
internal sealed class ModulePostProcessingBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource database) : IPostProcessingBroker
{
    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PostProcessingModel>>> ListAsync(Guid containerId, Guid postId, int limit = 50,
        Guid? sourceId = null, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
            return Result<IReadOnlyList<PostProcessingModel>>.Fail(AppErrorFactory.Validation("The result limit must be between 1 and 100."));
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        // Serialize membership changes and hold the visible post while reading its derived evidence.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        if (await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_posts WHERE container_id=@container AND id=@post AND purged_at IS NULL FOR SHARE",
                cancellationToken, ("container", containerId), ("post", postId)) is null)
            return Result<IReadOnlyList<PostProcessingModel>>.Fail(AppErrorFactory.NotFound("The post was not found."));

        await using var command = PostSql.Command(connection, transaction, """
            SELECT result.capture_id,link.source_id,result.link_id,result.run_id,result.processor,
                   COALESCE(reading.capture_id=result.capture_id,false),result.processed_at,run.status,result.result::text,
                   result.result IS NULL AND result.next_ready_at='infinity'::timestamptz
            FROM harvest_processing_results result
            JOIN harvest_post_links link ON link.container_id=result.container_id AND link.id=result.link_id
            JOIN harvest_runs run ON run.container_id=result.container_id AND run.id=result.run_id
            LEFT JOIN harvest_link_readings reading ON reading.container_id=link.container_id AND reading.link_id=link.id
            WHERE link.container_id=@container AND link.post_id=@post AND link.retired_at IS NULL
              AND (@source::uuid IS NULL OR link.source_id=@source)
            ORDER BY COALESCE(result.processed_at,run.queued_at) DESC,result.capture_id,result.processor
            LIMIT @limit
            """, ("container", containerId), ("post", postId), ("limit", limit), ("source", sourceId));
        var results = new List<PostProcessingModel>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                JsonElement? output = null;
                if (!reader.IsDBNull(8))
                {
                    using var document = JsonDocument.Parse(reader.GetString(8));
                    output = document.RootElement.Clone();
                }
                results.Add(new()
                {
                    CaptureId = reader.GetGuid(0), SourceId = reader.GetGuid(1), LinkId = reader.GetGuid(2),
                    RunId = reader.GetGuid(3), Processor = reader.GetString(4), IsCurrent = reader.GetBoolean(5),
                    ProcessedAtUtc = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    RunStatus = reader.GetString(7), Output = output, RequiresReview = reader.GetBoolean(9),
                });
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return Result<IReadOnlyList<PostProcessingModel>>.Ok(results);
    }
}
