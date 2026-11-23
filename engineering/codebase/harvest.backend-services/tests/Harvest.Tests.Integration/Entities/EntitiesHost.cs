using System.Text;
using System.Text.Json;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace Harvest.Tests.Integration.Entities;

/// <summary>Scripted extraction rows over the module's store: posts saved as captures, their done tags and extraction
/// answers inserted as the extraction lane would store them, and the entity broker under test. No AI call.</summary>
internal static class EntitiesHost
{
    internal static Task<TagsHost> StartAsync(Action<EntityOptions>? configure = null) =>
        TagsHost.StartAsync(enabled: false, ai: false, configure: services => services.AddHarvestEntities()
            .Configure(options => configure?.Invoke(options)));

    internal static IEntityBroker Entities(this TagsHost host) => host.Scoped.GetRequiredService<IEntityBroker>();

    /// <summary>Gets the read contract (step D) over the same store.</summary>
    internal static IEntitiesBroker Reads(this TagsHost host) => host.Scoped.GetRequiredService<IEntitiesBroker>();

    /// <summary>Saves a capture of <paramref name="key"/> on a source, with an optional author profile, and returns the post.</summary>
    internal static async Task<Guid> PostAsync(this TagsHost host, string key, string text, Guid? sourceId = null, string domain = "mstar.uz",
        string? author = null)
    {
        var capture = new Capture
        {
            ExternalKey = key, Url = $"https://{domain}/{key}", RequestUrl = $"https://{domain}/{key}", FinalUrl = $"https://{domain}/{key}",
            CapturedAtUtc = host.Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, HttpStatus = 200,
            ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Event, Title = key, Text = text, Language = new PostLanguage { Code = "uz" },
            Author = author is null ? null : new PostAuthor { Name = "Seller", Url = author },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        return PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, sourceId ?? host.SourceId, null, capture, envelope)).Post.Id;
    }

    /// <summary>Stores a done tags answer and a done extraction answer for the post's current content, as steps A and B do.</summary>
    internal static async Task ExtractAsync(this TagsHost host, Guid postId, string useCase, string answer, string domain)
    {
        string role = JsonDocument.Parse(answer).RootElement.GetProperty("relation").GetProperty("role").GetString()!;
        string hash = PostTagsContent.SqlHash("p.base");
        await host.ExecuteAsync($"""
            INSERT INTO harvest_post_tags(post_id,content_hash,container_id,revision,entity_type,domains,timeframe,status,created_at)
            SELECT p.id,{hash},p.container_id,p.revision,@type,CAST(@domains AS jsonb),'upcoming','done',now() FROM harvest_posts p WHERE p.id=@post
            ON CONFLICT DO NOTHING;
            INSERT INTO harvest_post_extractions(post_id,content_hash,use_case,container_id,revision,role,answer,status,created_at,updated_at)
            SELECT p.id,{hash},@useCase,p.container_id,p.revision,@role,CAST(@answer AS jsonb),'done',now(),now() FROM harvest_posts p WHERE p.id=@post
            """, ("type", useCase == "extract-event" ? "event" : "listing"),
            ("domains", JsonSerializer.Serialize(new[] { new { node = domain, primary = true } })), ("post", postId), ("useCase", useCase),
            ("role", role), ("answer", answer));
    }

    /// <summary>Saves an event post with its answer and resolves it.</summary>
    internal static async Task<(Guid PostId, EntityResolveOutcome Outcome)> EventAsync(this TagsHost host, string key, string answer,
        Guid? sourceId = null, string domain = "mstar.uz")
    {
        var postId = await host.PostAsync(key, $"{key} {Guid.NewGuid():N}", sourceId, domain);
        await host.ExtractAsync(postId, "extract-event", answer, "style-fashion/womens-fashion");
        return (postId, (await host.Entities().ResolveAsync(postId)).ValueOrThrow().Outcomes.Single());
    }

    /// <summary>Changes a post's text, so its content has a new hash, stores the new answer and resolves it again.</summary>
    internal static async Task<EntityResolveOutcome> ReviseAsync(this TagsHost host, Guid postId, string key, string useCase, string answer,
        Guid? sourceId = null, string domain = "mstar.uz", string? author = null, string domainNode = "style-fashion")
    {
        await host.PostAsync(key, $"{key} {Guid.NewGuid():N}", sourceId, domain, author);
        await host.ExtractAsync(postId, useCase, answer, domainNode);
        return (await host.Entities().ResolveAsync(postId)).ValueOrThrow().Outcomes.Single();
    }

    internal static async Task ExecuteAsync(this TagsHost host, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new Npgsql.NpgsqlConnection(host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    internal static object Run(string starts, string? ends = null, string certainty = "stated", string precision = "day") =>
        new { starts, ends, precision, certainty, evidence = starts };

    /// <summary>Builds a stored <c>extract-event</c> answer.</summary>
    internal static string Event(string en, string? original = null, object[]? runs = null, string? city = "Tashkent", string role = "announcement",
        string[]? changes = null, string? relationStatus = null, string status = "scheduled", string? editionLabel = null,
        string? venue = null) =>
        JsonSerializer.Serialize(new
        {
            name = new { en, original },
            editionLabel,
            occurrences = runs ?? [],
            place = new { venue, city, country = "Uzbekistan" },
            organizer = (string?)null,
            format = "runway",
            access = "ticketed",
            prices = new[] { new { amount = (decimal?)50_000m, currency = "UZS", label = "entry", evidence = "Kirish 50 000 so'm" } },
            registration = new { opens = (object?)null, deadline = (object?)null, link = (string?)null },
            participation = new[] { "visitor" },
            status,
            relation = new { role, changes = changes ?? [], status = relationStatus },
            fashion = (object?)null,
            flags = Array.Empty<object>(),
        });

    /// <summary>Builds a stored <c>extract-listing</c> answer (schema version 2) for a flat.</summary>
    internal static string Listing(decimal price, string district = "Yunusobod tumani", string city = "Tashkent", decimal area = 64m, int rooms = 2,
        string role = "announcement", string authorKind = "owner", string[]? categoryPath = null) =>
        JsonSerializer.Serialize(new
        {
            title = "2-room flat for rent",
            categoryPath = categoryPath ?? ["Недвижимость", "Квартиры", "Аренда"],
            propertyKind = "apartment",
            invalid = (string?)null,
            offerType = "rent",
            price = new { amount = price, currency = "usd", period = "month", negotiable = false, evidence = $"{price} USD" },
            item = new
            {
                propertyType = (string?)null, buildingType = (string?)null, yearBuilt = (int?)null, rooms = (int?)rooms, layout = (string?)null,
                areaM2 = new { total = (decimal?)area, living = (decimal?)null, kitchen = (decimal?)null }, floor = (int?)5, floors = (int?)9,
                ceilingM = (decimal?)null, bathrooms = new { count = (int?)null, type = (string?)null }, renovation = (string?)null, building = (string?)null,
            },
            furnished = "unknown",
            apartmentAmenities = Array.Empty<string>(),
            nearbyAmenities = Array.Empty<string>(),
            location = new { district, city, address = (string?)null },
            landmarks = Array.Empty<object>(),
            author = new { kind = authorKind },
            noAgents = false,
            relation = new { role, changes = Array.Empty<string>(), status = (string?)null },
            rentalTerms = (object?)null,
            acceptedTenants = Array.Empty<string>(),
            excludedTenants = Array.Empty<string>(),
            urgent = false,
            flags = Array.Empty<object>(),
        });
}
