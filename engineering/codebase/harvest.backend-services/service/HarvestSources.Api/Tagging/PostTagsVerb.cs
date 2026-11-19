using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HarvestSources.Api.Configurations;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace HarvestSources.Api.Tagging;

/// <summary>Operates the Layer 1 post-tags pass: registers its prompt, tags one post, queues stored posts and reports.</summary>
/// <remarks>
///   - <c>prompt --file &lt;draft.json | -&gt; [--by &lt;actor&gt;]</c> creates the versioned use case with the code-owned
///     schema (<c>KeepOutputs</c> true) and saves an immutable version; no prompt text lives in code
///   - <c>run --post &lt;id&gt; [--dry-run]</c> tags one post and prints the answer; a dry run writes nothing
///   - <c>backfill [--since &lt;date&gt;] [--limit N] [--source &lt;id&gt;]</c> queues stored posts without a done answer
///   - <c>report [--since &lt;date&gt;]</c> prints counts, flags, cost and p95 latency as one JSON line, no post text
/// </remarks>
public static partial class PostTagsVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "post-tags";

    private const string Usage = """
        Usage: post-tags prompt --file <draft.json | -> [--by <actor>]
               post-tags run --post <id> [--dry-run]
               post-tags backfill [--since <date>] [--limit <n>] [--source <id>]
               post-tags report [--since <date>]
        """;

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Runs one sub-command against the built internal host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb name.</param>
    /// <param name="input">Standard input, read when the prompt file is <c>-</c>.</param>
    /// <param name="output">Standard output for one JSON line.</param>
    /// <param name="error">Standard error for usage or a sanitized diagnostic.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>Zero on success, one for a refusal or a failed answer, and two for invalid arguments.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, Stream input,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (arguments.Count == 0 || !TryOptions(arguments.Skip(1).ToArray(), out var options))
            return await UsageAsync(error);

        await using var scope = services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetService<IPostTagsBroker>();
        if (tags is null)
        {
            await error.WriteLineAsync($"Enable {RentalTermsConfiguration.EnabledSetting} on the internal source host.");
            return 1;
        }
        try
        {
            return arguments[0] switch
            {
                "prompt" => await PromptAsync(scope.ServiceProvider, options, input, output, error, ct),
                "run" => await RunPostAsync(tags, options, output, error, ct),
                "backfill" => await BackfillAsync(tags, options, output, error, ct),
                "report" => await ReportAsync(tags, options, output, error, ct),
                _ => await UsageAsync(error),
            };
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            await error.WriteLineAsync("The store could not be reached. Check the SCFD harvest and ai migrations.");
            return 1;
        }
    }

    private static async Task<int> PromptAsync(IServiceProvider services, IReadOnlyDictionary<string, string?> options, Stream input,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!Only(options, "file", "by") || options.GetValueOrDefault("file") is not { } file)
            return await UsageAsync(error);
        string? by = options.GetValueOrDefault("by");
        if (services.GetService<IPromptsBroker>() is not { } prompts)
        {
            await error.WriteLineAsync($"Enable {RentalTermsConfiguration.EnabledSetting} on the internal source host.");
            return 1;
        }

        PromptDraft? draft;
        try
        {
            if (file == "-")
                draft = await JsonSerializer.DeserializeAsync<PromptDraft>(input, Wire, ct);
            else
            {
                await using var stream = File.OpenRead(file);
                draft = await JsonSerializer.DeserializeAsync<PromptDraft>(stream, Wire, ct);
            }
        }
        catch (JsonException)
        {
            await error.WriteLineAsync("Provide a JSON prompt draft with useCase, systemTemplate and userTemplate.");
            return 1;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await error.WriteLineAsync("The private prompt input could not be read.");
            return 1;
        }
        if (draft is not { UseCase: PostTagsConstants.UseCase } || string.IsNullOrWhiteSpace(draft.SystemTemplate)
            || string.IsNullOrWhiteSpace(draft.UserTemplate))
        {
            await error.WriteLineAsync($"Provide system and user templates for {PostTagsConstants.UseCase}.");
            return 1;
        }
        if (BindingError(draft) is { } binding)
        {
            await error.WriteLineAsync(binding);
            return 1;
        }

        if (!await EnsureUseCaseAsync(prompts, ct))
        {
            await error.WriteLineAsync("The post-tags use case requires the versioned SCFD prompt contract.");
            return 1;
        }
        var result = await prompts.SavePromptAsync(draft with { By = by?.Trim() ?? draft.By }, ct);
        if (result is not Result<PromptModel>.Success { Value: var saved })
        {
            await error.WriteLineAsync("The prompt was refused. Check the model match and prompt parameters.");
            return 1;
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            useCase = saved.UseCase, version = saved.Version, contentHash = saved.ContentHash, id = saved.Id,
        }));
        return 0;
    }

    private static async Task<int> RunPostAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!Only(options, "post", "dry-run") || !Guid.TryParseExact(options.GetValueOrDefault("post"), "D", out var postId))
            return await UsageAsync(error);
        var result = await tags.RunAsync(postId, options.ContainsKey("dry-run"), ct);
        if (result is not Result<PostTagsRunModel>.Success { Value: var run })
            return await RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(run, JsonOptionsConstants.Default));
        return run.Status == "done" ? 0 : 1;
    }

    private static async Task<int> BackfillAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!Only(options, "since", "limit", "source") || !TrySince(options, out var since)
            || !TryInt(options, "limit", 1000, out var limit))
            return await UsageAsync(error);
        Guid? source = null;
        if (options.TryGetValue("source", out var raw))
        {
            if (!Guid.TryParseExact(raw, "D", out var parsed))
                return await UsageAsync(error);
            source = parsed;
        }
        var result = await tags.BackfillAsync(new PostTagsBackfillRequest { SinceUtc = since, Limit = limit, SourceId = source }, ct);
        if (result is not Result<int>.Success { Value: var queued })
            return await RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(new { queued }));
        return 0;
    }

    private static async Task<int> ReportAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!Only(options, "since") || !TrySince(options, out var since))
            return await UsageAsync(error);
        var result = await tags.ReportAsync(since, ct);
        if (result is not Result<PostTagsReportModel>.Success { Value: var report })
            return await RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptionsConstants.Default));
        return 0;
    }

    private static async Task<bool> EnsureUseCaseAsync(IPromptsBroker prompts, CancellationToken ct)
    {
        var existing = await prompts.GetUseCaseAsync(PostTagsConstants.UseCase, ct);
        if (existing is Result<UseCaseModel>.Success { Value: var useCase })
            return Compatible(useCase);

        return existing is Result<UseCaseModel>.Failure { Error.Type: AppErrorType.NotFound }
            && await prompts.PutUseCaseAsync(new()
            {
                Key = PostTagsConstants.UseCase,
                Description = "Tag every SCFD post: entity type, IAB domains, English topics and routing fields.",
                PromptMode = PromptMode.Versioned,
                OutputSchema = PostTagsDefinition.OutputSchema,
                KeepOutputs = true,
            }, ct) is Result<UseCaseModel>.Success;
    }

    private static bool Compatible(UseCaseModel useCase)
    {
        if (useCase is not { PromptMode: PromptMode.Versioned, KeepOutputs: true, OutputSchema: { } schema })
            return false;
        using var expected = JsonDocument.Parse(PostTagsDefinition.OutputSchema);
        using var actual = JsonDocument.Parse(schema);
        return JsonElement.DeepEquals(expected.RootElement, actual.RootElement);
    }

    /// <summary>Names a template placeholder the pass never supplies, or a required one that is missing.</summary>
    private static string? BindingError(PromptDraft draft)
    {
        var unknown = Placeholders().Matches(draft.SystemTemplate + "\n" + draft.UserTemplate)
            .Select(match => match.Groups["name"].Value)
            .FirstOrDefault(name => !PostTagsDefinition.Variables.Contains(name, StringComparer.Ordinal));
        if (unknown is not null)
            return $"Use only the post-tags variables: {string.Join(", ", PostTagsDefinition.Variables)}.";
        if (!Binds(draft.SystemTemplate, "taxonomy"))
            return "Include the {{taxonomy}} variable in systemTemplate.";
        return Binds(draft.UserTemplate, "text") ? null : "Include the {{text}} variable in userTemplate.";
    }

    private static bool Binds(string template, string name) =>
        Placeholders().Matches(template).Any(match => match.Groups["name"].Value == name);

    private static async Task<int> RefusedAsync<T>(TextWriter error, Result<T> result) where T : notnull
    {
        await error.WriteLineAsync(result is Result<T>.Failure failure ? failure.Error.Message : "The request was refused.");
        return 1;
    }

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }

    /// <summary>Reads <c>--name value</c> pairs; <c>--dry-run</c> takes no value. A repeated or valueless option fails.</summary>
    private static bool TryOptions(IReadOnlyList<string> arguments, out Dictionary<string, string?> options)
    {
        options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal) || arguments[index].Length == 2)
                return false;
            string name = arguments[index][2..];
            if (name == "dry-run")
            {
                if (!options.TryAdd(name, null))
                    return false;
                continue;
            }
            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(arguments[index + 1]) || !options.TryAdd(name, arguments[++index]))
                return false;
        }
        return true;
    }

    private static bool Only(IReadOnlyDictionary<string, string?> options, params string[] allowed) =>
        options.Keys.All(key => allowed.Contains(key, StringComparer.Ordinal));

    private static bool TrySince(IReadOnlyDictionary<string, string?> options, out DateTimeOffset? since)
    {
        since = null;
        if (!options.TryGetValue("since", out var raw))
            return true;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        since = parsed.ToUniversalTime();
        return true;
    }

    private static bool TryInt(IReadOnlyDictionary<string, string?> options, string name, int fallback, out int value)
    {
        value = fallback;
        return !options.TryGetValue(name, out var raw)
            || int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholders();
}
