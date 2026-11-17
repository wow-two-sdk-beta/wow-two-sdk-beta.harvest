using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HarvestSources.Api.Configurations;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestSources.Api.Marketplace;

/// <summary>Registers a versioned OLX rental-terms prompt from a private JSON draft.</summary>
public static partial class RentalTermsPromptVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "rental-terms";

    private const string Usage = "Usage: rental-terms prompt --file <draft.json | -> --by <actor>";

    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads a prompt draft from the named file or standard input and prints its saved version metadata.</summary>
    /// <param name="services">The built internal host's services.</param>
    /// <param name="arguments">The arguments after the verb name.</param>
    /// <param name="input">Standard input, used when the file argument is <c>-</c>.</param>
    /// <param name="output">Standard output for the saved version metadata.</param>
    /// <param name="error">Standard error for usage or a sanitized diagnostic.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>Zero for a saved prompt, one for a refusal, and two for invalid arguments.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, Stream input,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        (string? file, string? actor) = arguments switch
        {
            ["prompt", "--file", var path, "--by", var by] => (path, by),
            ["prompt", "--by", var by, "--file", var path] => (path, by),
            _ => (null, null),
        };
        if (string.IsNullOrWhiteSpace(file) || file.StartsWith("--", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(actor) || actor.StartsWith("--", StringComparison.Ordinal))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IPromptsBroker>() is not { } prompts)
        {
            await error.WriteLineAsync($"Enable {RentalTermsConfiguration.EnabledSetting} on the internal source host.");
            return 1;
        }

        PromptDraft? draft;
        try
        {
            draft = await ReadDraftAsync(file, input, ct);
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
        if (draft is not { UseCase: RentalTermsDefinition.UseCase, SystemTemplate: not null }
            || string.IsNullOrWhiteSpace(draft.UserTemplate))
        {
            await error.WriteLineAsync($"Provide system and user templates for {RentalTermsDefinition.UseCase}.");
            return 1;
        }
        if (!DescriptionBinding().IsMatch(draft.UserTemplate))
        {
            await error.WriteLineAsync("Include the {{description}} variable in userTemplate.");
            return 1;
        }

        try
        {
            if (!await EnsureUseCaseAsync(prompts, ct))
            {
                await error.WriteLineAsync("The rental-terms use case requires the versioned SCFD prompt contract.");
                return 1;
            }
            var result = await prompts.SavePromptAsync(draft with { By = actor.Trim() }, ct);
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
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            await error.WriteLineAsync("The prompt registry write could not be confirmed. Check the SCFD ai migration.");
            return 1;
        }
    }

    private static async Task<PromptDraft?> ReadDraftAsync(string file, Stream input, CancellationToken ct)
    {
        if (file == "-")
            return await JsonSerializer.DeserializeAsync<PromptDraft>(input, Wire, ct);
        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<PromptDraft>(stream, Wire, ct);
    }

    private static async Task<bool> EnsureUseCaseAsync(IPromptsBroker prompts, CancellationToken ct)
    {
        var existing = await prompts.GetUseCaseAsync(RentalTermsDefinition.UseCase, ct);
        if (existing is Result<UseCaseModel>.Success { Value: var useCase })
            return CompatibleUseCase(useCase);

        return existing is Result<UseCaseModel>.Failure { Error.Type: AppErrorType.NotFound }
            && await prompts.PutUseCaseAsync(new()
            {
                Key = RentalTermsDefinition.UseCase,
                Description = "Extract evidence-backed rental terms from saved OLX descriptions.",
                PromptMode = PromptMode.Versioned,
                OutputSchema = RentalTermsDefinition.OutputSchema,
                KeepOutputs = false,
            }, ct) is Result<UseCaseModel>.Success;
    }

    private static bool CompatibleUseCase(UseCaseModel useCase)
    {
        if (useCase is not
            { PromptMode: PromptMode.Versioned, KeepOutputs: false, Route.Count: 0, OutputSchema: { } schema })
            return false;

        using var expected = JsonDocument.Parse(RentalTermsDefinition.OutputSchema);
        using var actual = JsonDocument.Parse(schema);
        return JsonElement.DeepEquals(expected.RootElement, actual.RootElement);
    }

    [GeneratedRegex(@"\{\{\s*description\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex DescriptionBinding();
}
