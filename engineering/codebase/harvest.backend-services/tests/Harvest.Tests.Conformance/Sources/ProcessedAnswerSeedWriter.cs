using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Sources;

namespace Harvest.Tests.Conformance.Sources;

/// <summary>Writes a post's seeded AI answers into a PostgreSQL store as the passes leave them: a
/// <c>harvest_post_tags</c> row and, when seeded, a <c>harvest_post_extractions</c> row whose answer has the use case's
/// own shape, both keyed by the post's current title-and-text hash.</summary>
internal static class ProcessedAnswerSeedWriter
{
    /// <summary>The SQL twin of the passes' content hash: SHA-256 of title, unit separator, text.</summary>
    private const string Hash = "sha256(convert_to(COALESCE(p.base->>'title','') || chr(31) || COALESCE(p.base->>'text',''),'UTF8'))";

    internal static async Task WriteAsync(string connectionString, ProcessedAnswerSeed seed)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var tags = new NpgsqlCommand($"""
            INSERT INTO harvest_post_tags(post_id,content_hash,container_id,revision,prompt_version,model,entity_type,domains,topics,about,
                timeframe,confidence,title_en,flags,status,error,created_at)
            SELECT p.id,{Hash},p.container_id,p.revision,@prompt,@model,@type,@domains,@topics,@about,@timeframe,@confidence,@titleEn,
                @flags,@status,@error,@at
            FROM harvest_posts p WHERE p.id=@post
            """, connection, transaction))
        {
            tags.Parameters.AddWithValue("post", seed.PostId);
            tags.Parameters.AddWithValue("prompt", NpgsqlDbType.Integer, (object?)seed.PromptVersion ?? DBNull.Value);
            tags.Parameters.AddWithValue("model", NpgsqlDbType.Text, (object?)seed.Model ?? DBNull.Value);
            tags.Parameters.AddWithValue("type", NpgsqlDbType.Text, (object?)seed.EntityType ?? DBNull.Value);
            tags.Parameters.AddWithValue("domains", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(seed.Domains.Select((key, index) => new { node = key, primary = index == 0 })));
            tags.Parameters.AddWithValue("topics", seed.Topics.ToArray());
            tags.Parameters.AddWithValue("about", NpgsqlDbType.Text, (object?)seed.About ?? DBNull.Value);
            tags.Parameters.AddWithValue("timeframe", NpgsqlDbType.Text, (object?)seed.Timeframe ?? DBNull.Value);
            tags.Parameters.AddWithValue("confidence", NpgsqlDbType.Numeric, (object?)seed.Confidence ?? DBNull.Value);
            tags.Parameters.AddWithValue("titleEn", NpgsqlDbType.Text, (object?)seed.TitleEn ?? DBNull.Value);
            tags.Parameters.AddWithValue("flags", NpgsqlDbType.Jsonb, Flags(seed.Flags));
            tags.Parameters.AddWithValue("status", seed.Status);
            tags.Parameters.AddWithValue("error", NpgsqlDbType.Text, (object?)seed.Error ?? DBNull.Value);
            tags.Parameters.AddWithValue("at", seed.AnsweredAtUtc);
            if (await tags.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("The seeded post is not stored.");
        }

        if (seed.Extraction is { } extraction)
        {
            await using var extract = new NpgsqlCommand($"""
                INSERT INTO harvest_post_extractions(post_id,content_hash,use_case,container_id,revision,prompt_version,model,role,answer,flags,
                    status,created_at,updated_at)
                SELECT p.id,{Hash},@useCase,p.container_id,p.revision,@prompt,@model,@role,@answer,@flags,@status,@at,@at
                FROM harvest_posts p WHERE p.id=@post
                """, connection, transaction);
            extract.Parameters.AddWithValue("post", seed.PostId);
            extract.Parameters.AddWithValue("useCase", extraction.UseCase);
            extract.Parameters.AddWithValue("prompt", NpgsqlDbType.Integer, (object?)extraction.PromptVersion ?? DBNull.Value);
            extract.Parameters.AddWithValue("model", NpgsqlDbType.Text, (object?)extraction.Model ?? DBNull.Value);
            extract.Parameters.AddWithValue("role", NpgsqlDbType.Text, (object?)extraction.Role ?? DBNull.Value);
            extract.Parameters.AddWithValue("answer", NpgsqlDbType.Jsonb, extraction.Status == "done" ? Answer(extraction) : DBNull.Value);
            extract.Parameters.AddWithValue("flags", NpgsqlDbType.Jsonb, Flags(extraction.Flags));
            extract.Parameters.AddWithValue("status", extraction.Status);
            extract.Parameters.AddWithValue("at", seed.AnsweredAtUtc);
            await extract.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static string Flags(IEnumerable<WoW.Two.Sdk.Beta.Be.Harvest.Sources.ProcessedFlagModel> flags) =>
        JsonSerializer.Serialize(flags.Select(flag => new { field = flag.Field, reason = flag.Reason, proposed = flag.Proposed }));

    /// <summary>Writes the summary back in the answer's own shape: the fields the read summarizes, the rest left out.</summary>
    private static string Answer(ProcessedExtractionSeed extraction)
    {
        var relation = new { role = extraction.Role, changes = Array.Empty<string>(), status = (string?)null };
        if (extraction.Event is { } item)
            return JsonSerializer.Serialize(new
            {
                name = new { en = item.Name, original = item.OriginalName },
                editionLabel = item.EditionLabel,
                occurrences = item.Occurrences.Select(run => new
                {
                    starts = run.Starts, ends = run.Ends, precision = run.Precision, certainty = run.Certainty, evidence = run.Starts,
                }),
                place = new { venue = item.Venue, city = item.City, country = item.Country },
                format = item.Format,
                access = item.Access,
                prices = item.Prices.Select(price => new { amount = price.Amount, currency = price.Currency, label = price.Label, evidence = "" }),
                status = item.Status,
                relation,
            });
        var listing = extraction.Listing ?? new();
        return JsonSerializer.Serialize(new
        {
            offerType = listing.OfferType,
            price = listing.Price is { } price
                ? new { amount = price.Amount, currency = price.Currency, period = price.Label, evidence = "" }
                : null,
            location = new { district = listing.District, city = listing.City, address = (string?)null },
            item = new { rooms = listing.Rooms, areaM2 = listing.AreaM2 },
            relation,
        });
    }
}
