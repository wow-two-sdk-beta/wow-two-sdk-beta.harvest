using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace HarvestSources.Api.Configurations;

/// <summary>Reads the options of the AI host verbs (<c>post-tags</c>, <c>extract</c>) and registers their versioned use
/// cases: <c>--name value</c> pairs, <c>--dry-run</c> without a value, dates, counts and prompt drafts.</summary>
internal static partial class VerbArguments
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads <c>--name value</c> pairs; <c>--dry-run</c> and <c>--all-sources</c> take no value. A repeated or
    /// valueless option fails.</summary>
    internal static bool TryOptions(IReadOnlyList<string> arguments, out Dictionary<string, string?> options)
    {
        options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Count; index++)
        {
            if (!arguments[index].StartsWith("--", StringComparison.Ordinal) || arguments[index].Length == 2)
                return false;
            string name = arguments[index][2..];
            if (name is "dry-run" or "all-sources")
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

    /// <summary>Whether every given option is one of <paramref name="allowed"/>.</summary>
    internal static bool Only(IReadOnlyDictionary<string, string?> options, params string[] allowed) =>
        options.Keys.All(key => allowed.Contains(key, StringComparer.Ordinal));

    /// <summary>Reads an optional <c>--since</c> date as UTC.</summary>
    internal static bool TrySince(IReadOnlyDictionary<string, string?> options, out DateTimeOffset? since)
    {
        since = null;
        if (!options.TryGetValue("since", out var raw))
            return true;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        since = parsed.ToUniversalTime();
        return true;
    }

    /// <summary>Reads an optional positive integer, or takes <paramref name="fallback"/>.</summary>
    internal static bool TryInt(IReadOnlyDictionary<string, string?> options, string name, int fallback, out int value)
    {
        value = fallback;
        return !options.TryGetValue(name, out var raw)
            || int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    /// <summary>Reads an optional comma-separated list of ids; false when one is not a GUID.</summary>
    internal static bool TryIds(IReadOnlyDictionary<string, string?> options, string name, out IReadOnlyList<Guid> ids)
    {
        ids = [];
        if (!options.TryGetValue(name, out var raw))
            return true;
        var parsed = new List<Guid>();
        foreach (var part in (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParseExact(part, "D", out var id))
                return false;
            parsed.Add(id);
        }
        ids = parsed;
        return parsed.Count > 0;
    }

    /// <summary>Reads an optional comma-separated list of words.</summary>
    internal static IReadOnlyList<string> List(IReadOnlyDictionary<string, string?> options, string name) =>
        options.TryGetValue(name, out var raw) ? (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    /// <summary>Reads a JSON prompt draft from a file or, for <c>-</c>, standard input; null with a message when unreadable.</summary>
    internal static async Task<(PromptDraft? Draft, string? Error)> ReadDraftAsync(string file, Stream input, CancellationToken ct)
    {
        try
        {
            if (file == "-")
                return (await JsonSerializer.DeserializeAsync<PromptDraft>(input, Wire, ct), null);
            await using var stream = File.OpenRead(file);
            return (await JsonSerializer.DeserializeAsync<PromptDraft>(stream, Wire, ct), null);
        }
        catch (JsonException)
        {
            return (null, "Provide a JSON prompt draft with useCase, systemTemplate and userTemplate.");
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, "The private prompt input could not be read.");
        }
    }

    /// <summary>Creates a versioned use case with the code-owned schema and kept outputs, or confirms an existing one is
    /// compatible; false when the existing one differs.</summary>
    internal static async Task<bool> EnsureUseCaseAsync(IPromptsBroker prompts, string key, string description, string schema, CancellationToken ct)
    {
        var existing = await prompts.GetUseCaseAsync(key, ct);
        if (existing is Result<UseCaseModel>.Success { Value: var useCase })
            return Compatible(useCase, schema);

        return existing is Result<UseCaseModel>.Failure { Error.Type: AppErrorType.NotFound }
            && await prompts.PutUseCaseAsync(new()
            {
                Key = key, Description = description, PromptMode = PromptMode.Versioned, OutputSchema = schema, KeepOutputs = true,
            }, ct) is Result<UseCaseModel>.Success;
    }

    /// <summary>Whether a registered use case keeps versions and outputs and answers with <paramref name="schema"/>.</summary>
    internal static bool Compatible(UseCaseModel useCase, string schema)
    {
        if (useCase is not { PromptMode: PromptMode.Versioned, KeepOutputs: true, OutputSchema: { } actual })
            return false;
        using var expected = JsonDocument.Parse(schema);
        using var stored = JsonDocument.Parse(actual);
        return JsonElement.DeepEquals(expected.RootElement, stored.RootElement);
    }

    /// <summary>Names the first template placeholder outside <paramref name="variables"/>, or null when all are known.</summary>
    internal static string? UnknownPlaceholder(PromptDraft draft, IReadOnlyList<string> variables) =>
        Placeholders().Matches(draft.SystemTemplate + "\n" + draft.UserTemplate)
            .Select(match => match.Groups["name"].Value)
            .FirstOrDefault(name => !variables.Contains(name, StringComparer.Ordinal));

    /// <summary>Whether <paramref name="template"/> binds the variable <paramref name="name"/>.</summary>
    internal static bool Binds(string template, string name) =>
        Placeholders().Matches(template).Any(match => match.Groups["name"].Value == name);

    /// <summary>Writes a refusal's message and returns exit code one.</summary>
    internal static async Task<int> RefusedAsync<T>(TextWriter error, Result<T> result) where T : notnull
    {
        await error.WriteLineAsync(result is Result<T>.Failure failure ? failure.Error.Message : "The request was refused.");
        return 1;
    }

    [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholders();
}
