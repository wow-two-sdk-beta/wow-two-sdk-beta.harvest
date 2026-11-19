using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Tests.E2E;

/// <summary>The <c>entities</c> host verb on the source host over scripted extraction rows: resolve, backfill, show,
/// search, merge, split and report, each printing JSON and never post text.</summary>
public sealed class EntitiesVerbTests
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=synthetic;Username=synthetic;Timeout=1";
    private const string Hash = "sha256(convert_to(COALESCE(p.base->>'title','') || chr(31) || COALESCE(p.base->>'text',''),'UTF8'))";
    private const string PrivateText = "Moda haftaligi 19-24 oktabr kunlari Uzexpocentre'da bo'ladi";

    [Fact]
    public async Task Host_binds_entity_options_and_refuses_inverted_thresholds()
    {
        await using var host = Build(new Dictionary<string, string?>
        {
            [$"{EntityConstants.SectionName}:Enabled"] = "true",
            [$"{EntityConstants.SectionName}:MinMatchConfidence"] = "0.8",
            [$"{EntityConstants.SectionName}:EventDateWindowDays"] = "14",
            [$"{EntityConstants.SectionName}:Cadence:AnnualDays"] = "30",
        });

        var options = host.Services.GetRequiredService<IOptions<EntityOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.MinMatchConfidence.Should().Be(0.8);
        options.EventDateWindowDays.Should().Be(14);
        options.Cadence.AnnualDays.Should().Be(30);
        await using (var defaults = Build(new Dictionary<string, string?>()))
            defaults.Services.GetRequiredService<IOptions<EntityOptions>>().Value.Enabled.Should().BeFalse("the layer is off by default");

        await using var invalid = Build(new Dictionary<string, string?>
        {
            [$"{EntityConstants.SectionName}:CandidateFloor"] = "0.9",
            [$"{EntityConstants.SectionName}:MinMatchConfidence"] = "0.7",
        });
        FluentActions.Invoking(() => invalid.Services.GetRequiredService<IOptions<EntityOptions>>().Value)
            .Should().Throw<OptionsValidationException>().WithMessage("*CandidateFloor*");
    }

    [Fact]
    public async Task Verbs_resolve_read_correct_and_report_entities_without_post_text()
    {
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = Build(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = database,
            [$"{EntityConstants.SectionName}:Enabled"] = "true",
            [$"{EntityConstants.SectionName}:CandidateFloor"] = "0.6",
        });
        await host.ConfigureAsync();
        var (container, first, second) = await SourcesAsync(host.Services);
        var p1 = await PostAsync(host.Services, container, first, "fashion.example", "tfw-2025", PrivateText);
        var p2 = await PostAsync(host.Services, container, second, "moda.example", "nedelya", "Неделя моды в Ташкенте пройдёт с 19 по 24 октября");
        var p3 = await PostAsync(host.Services, container, first, "fashion.example", "ufw", "O'zbekiston moda haftaligi 2024");
        var p4 = await PostAsync(host.Services, container, first, "fashion.example", "sfw", "Samarqand moda haftaligi");
        await ExtractAsync(database, p1, Event("Tashkent Fashion Week", "Toshkent moda haftaligi", "2025-10-20", "Tashkent"));
        await ExtractAsync(database, p2, Event("Tashkent Fashion Week", "Неделя моды в Ташкенте", "2026-10-19", "Ташкент"));
        await ExtractAsync(database, p3, Event("Uzbekistan Fashion Week", "O'zbekiston moda haftaligi", "2024-10-21", "Tashkent"));
        await ExtractAsync(database, p4, Event("Samarkand Fashion Week", null, "2026-04-10", "Samarkand"));

        var dry = await RunAsync(host.Services, ["resolve", "--post", p1.ToString(), "--dry-run"]);
        dry.Exit.Should().Be(0, dry.Error);
        Line(dry.Output).GetProperty("dryRun").GetBoolean().Should().BeTrue();
        (await ScalarAsync<long>(database, "SELECT count(*) FROM harvest_entities")).Should().Be(0);

        var one = Outcome(await RunAsync(host.Services, ["resolve", "--post", p1.ToString()]));
        one.GetProperty("outcome").GetString().Should().Be("created");
        var entity = one.GetProperty("entityId").GetGuid();
        Outcome(await RunAsync(host.Services, ["resolve", "--post", p2.ToString()])).GetProperty("entityId").GetGuid().Should().Be(entity);
        var other = Outcome(await RunAsync(host.Services, ["resolve", "--post", p3.ToString()])).GetProperty("entityId").GetGuid();
        var samarkand = Outcome(await RunAsync(host.Services, ["resolve", "--post", p4.ToString()])).GetProperty("entityId").GetGuid();
        new[] { other, samarkand }.Should().NotContain(entity).And.OnlyHaveUniqueItems();

        var show = await RunAsync(host.Services, ["show", "--id", entity.ToString()]);
        show.Exit.Should().Be(0, show.Error);
        using (var detail = JsonDocument.Parse(show.Output))
        {
            detail.RootElement.GetProperty("entity").GetProperty("name").GetString().Should().Be("Tashkent Fashion Week");
            detail.RootElement.GetProperty("entity").GetProperty("cadence").GetString().Should().Be("annual");
            detail.RootElement.GetProperty("years").EnumerateArray().Select(year => year.GetProperty("year").GetInt32()).Should().Equal(2025, 2026, 2027);
            detail.RootElement.GetProperty("relations").EnumerateArray()
                .Select(relation => (relation.GetProperty("postId").GetGuid(), relation.GetProperty("role").GetString(), relation.GetProperty("source").GetString()))
                .Should().Equal((p1, "announcement", "fashion.example"), (p2, "announcement", "moda.example"));
        }

        var search = await RunAsync(host.Services, ["search", "--q", "Неделя моды Ташкент", "--type", "event", "--domain", "style-fashion"]);
        search.Exit.Should().Be(0, search.Error);
        using (var hits = JsonDocument.Parse(search.Output))
        {
            var hit = hits.RootElement.EnumerateArray().First();
            hit.GetProperty("id").GetGuid().Should().Be(entity);
            hit.GetProperty("years").EnumerateArray().Select(year => year.GetInt32()).Should().Equal(2025, 2026);
            hit.GetProperty("predicted").GetString().Should().Be("2027-10-19");
        }

        var merge = await RunAsync(host.Services, ["merge", "--from", other.ToString(), "--into", entity.ToString()]);
        merge.Exit.Should().Be(0, merge.Error);
        Line(merge.Output).GetProperty("relations").GetInt32().Should().Be(1);
        var split = await RunAsync(host.Services, ["split", "--a", entity.ToString(), "--b", samarkand.ToString()]);
        split.Exit.Should().Be(0, split.Error);
        Line(split.Output).GetProperty("created").GetBoolean().Should().BeTrue();
        var refused = await RunAsync(host.Services, ["merge", "--from", samarkand.ToString(), "--into", entity.ToString()]);
        refused.Exit.Should().Be(1);
        refused.Error.Should().Contain("kept apart");

        var backfill = await RunAsync(host.Services, ["backfill", "--since", "2020-01-01", "--limit", "10"]);
        backfill.Exit.Should().Be(0, backfill.Error);
        backfill.Output.Trim().Should().Be("""{"queued":0}""");

        var report = await RunAsync(host.Services, ["report"]);
        report.Exit.Should().Be(0, report.Error);
        var counts = Line(report.Output);
        counts.GetProperty("types").GetProperty("event").GetInt64().Should().Be(2);
        counts.GetProperty("cadences").GetProperty("annual").GetInt64().Should().Be(1);
        counts.GetProperty("merged").GetInt64().Should().Be(1);
        counts.GetProperty("keptApart").GetInt64().Should().Be(1);

        foreach (var output in new[] { dry.Output, show.Output, search.Output, merge.Output, split.Output, report.Output })
            output.Should().NotContain("Uzexpocentre").And.NotContain("пройдёт").And.NotContain("19-24 oktabr");

        (await RunAsync(host.Services, ["resolve", "--post", Guid.NewGuid().ToString()])).Exit.Should().Be(1);
        (await RunAsync(host.Services, ["resolve", "--post", "not-a-guid"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["show"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["search", "--type", "event"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["search", "--q", "fashion", "--type", "news"])).Exit.Should().Be(1);
        (await RunAsync(host.Services, ["merge", "--from", entity.ToString()])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["report", "--since", "2020-01-01"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, ["unknown"])).Exit.Should().Be(2);
        (await RunAsync(host.Services, [])).Exit.Should().Be(2);
    }

    [Fact]
    public async Task Backfill_refuses_while_the_layer_is_off()
    {
        await using var host = Build(new Dictionary<string, string?> { ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync() });
        await host.ConfigureAsync();

        var run = await RunAsync(host.Services, ["backfill"]);

        run.Exit.Should().Be(1);
        run.Error.Should().Contain("Harvest:Entities:Enabled");
    }

    private static JsonElement Line(string output)
    {
        output.Trim().Should().NotContain("\n", "the verb prints one JSON line");
        return JsonDocument.Parse(output).RootElement.Clone();
    }

    private static JsonElement Outcome((int Exit, string Output, string Error) run)
    {
        run.Exit.Should().Be(0, run.Error);
        return Line(run.Output).GetProperty("outcomes")[0];
    }

    private static string Event(string en, string? original, string starts, string city) => JsonSerializer.Serialize(new
    {
        name = new { en, original },
        editionLabel = (string?)null,
        occurrences = new[]
        {
            new { starts, ends = (string?)DateOnly.Parse(starts).AddDays(5).ToString("yyyy-MM-dd"), precision = "day", certainty = "stated", evidence = starts },
        },
        place = new { venue = (string?)null, city, country = "Uzbekistan" },
        organizer = (string?)null,
        format = "runway",
        access = "ticketed",
        prices = Array.Empty<object>(),
        registration = new { opens = (object?)null, deadline = (object?)null, link = (string?)null },
        participation = new[] { "visitor" },
        status = "scheduled",
        relation = new { role = "announcement", changes = Array.Empty<string>(), status = (string?)null },
        fashion = (object?)null,
        flags = Array.Empty<object>(),
    });

    private static async Task<(Guid Container, Guid First, Guid Second)> SourcesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var container = (await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).ValueOrThrow();
        var sources = new List<Guid>();
        foreach (var handle in new[] { "fashion.example", "moda.example" })
            sources.Add((await registry.RegisterSourceAsync(new SourceRegistration
            {
                ContainerId = container.Id, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
                Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
            })).ValueOrThrow().Id);
        return (container.Id, sources[0], sources[1]);
    }

    private static async Task<Guid> PostAsync(IServiceProvider services, Guid container, Guid source, string domain, string key, string text)
    {
        await using var scope = services.CreateAsyncScope();
        var capture = new Capture
        {
            ExternalKey = key, Url = $"https://{domain}/{key}", RequestUrl = $"https://{domain}/{key}", FinalUrl = $"https://{domain}/{key}",
            CapturedAtUtc = DateTimeOffset.UtcNow, Body = Encoding.UTF8.GetBytes(text), ContentProjection = text, HttpStatus = 200,
            ContentType = "text/html",
        };
        var envelope = new PostEnvelope
        {
            Kind = PostKind.Event, Title = key, Text = text, Language = new PostLanguage { Code = "uz" },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };
        return (await scope.ServiceProvider.GetRequiredService<IPostBroker>().SaveCaptureAsync(container, source, null, capture, envelope))
            .ValueOrThrow().Post.Id;
    }

    private static async Task ExtractAsync(string connectionString, Guid postId, string answer)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            INSERT INTO harvest_post_tags(post_id,content_hash,container_id,revision,entity_type,domains,timeframe,status,created_at)
            SELECT p.id,{Hash},p.container_id,p.revision,'event',CAST(@domains AS jsonb),'upcoming','done',now()
            FROM harvest_posts p WHERE p.id=@post;
            INSERT INTO harvest_post_extractions(post_id,content_hash,use_case,container_id,revision,role,answer,status,created_at,updated_at)
            SELECT p.id,{Hash},'extract-event',p.container_id,p.revision,'announcement',CAST(@answer AS jsonb),'done',now(),now()
            FROM harvest_posts p WHERE p.id=@post
            """, connection);
        command.Parameters.AddWithValue("post", postId);
        command.Parameters.AddWithValue("answer", answer);
        command.Parameters.AddWithValue("domains", "[{\"node\":\"style-fashion\",\"primary\":true}]");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static WebApplication Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = UnreachableDatabase,
            ["Harvest:Operator:ApiKey"] = "hv_" + new string('r', 32),
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Configure();
        return builder.Build();
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(IServiceProvider services, string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await EntitiesVerb.RunAsync(services, args, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
