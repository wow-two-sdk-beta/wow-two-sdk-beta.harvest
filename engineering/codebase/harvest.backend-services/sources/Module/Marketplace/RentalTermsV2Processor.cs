using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Produces reviewable rental terms from the description in a saved OLX capture.</summary>
internal sealed class RentalTermsV2Processor(IServiceProvider services, IOptions<RentalTermsOptions> options) : ICaptureProcessor
{
    private const int MaxInputLength = 12_000;
    private const string PromptUnavailableMessage = "The exact rental-terms prompt and schema are unavailable.";

    public string Key => RentalTermsDefinition.ProcessorKey;

    public string Label => "Rental terms with evidence";

    public bool RequiresAi => true;

    public bool Serves(SourceModel source) =>
        source.Family == SourceFamily.Site && source.Handle is "www.olx.uz" or "olx.uz";

    public IReadOnlyList<string> OperationKeys => ["resolve-model", "resolve-prompt", "call-model", "validate-result"];

    public AppError? ValidateConfiguration(PipelineStageOptions stage) => ConfigurationError(stage.Model, stage.PromptVersion);

    public async Task<AppError?> ValidateConfigurationAsync(PipelineStageOptions stage, CancellationToken cancellationToken = default)
    {
        if (ValidateConfiguration(stage) is { } error)
            return error;
        return await ResolvePromptHashAsync(stage.Model!, stage.PromptVersion!.Value, cancellationToken) is null
            ? AppErrorFactory.Conflict(PromptUnavailableMessage)
            : null;
    }

    public async Task<CaptureProcessResult> ProcessAsync(CaptureProcessInput input, CancellationToken cancellationToken = default)
    {
        var error = await input.Trace.RunOperationAsync("resolve-model",
            () => Task.FromResult(ValidateInput(input)),
            result => result is null ? new() : new("failed", Message: result.Message));
        if (error is not null)
            return Failure(FetchOutcome.CapabilityUnavailable, error.Message);

        if (string.IsNullOrWhiteSpace(input.Text))
            return await input.Trace.RunOperationAsync("call-model",
                () => Task.FromResult(Complete(input, RentalTermsAnswer.Empty, truncated: false, run: null)),
                _ => new("skipped", Input: 0, Message: "No saved description to process."));

        string route = input.Model!;
        int version = input.PromptVersion!.Value;
        string? promptHash = await input.Trace.RunOperationAsync("resolve-prompt",
            () => ResolvePromptHashAsync(route, version, cancellationToken),
            hash => hash is null ? new("failed", Message: PromptUnavailableMessage) : new());
        if (promptHash is null)
            return Failure(FetchOutcome.CapabilityUnavailable, PromptUnavailableMessage);

        bool truncated = input.Text.Length > MaxInputLength;
        int end = truncated && char.IsHighSurrogate(input.Text[MaxInputLength - 1]) ? MaxInputLength - 1 : MaxInputLength;
        string description = truncated ? input.Text[..end] : input.Text;
        var runner = services.GetRequiredService<IAiRunner>();
        var response = await input.Trace.RunOperationAsync("call-model", () => runner.RunAsync(new()
        {
            UseCase = RentalTermsDefinition.UseCase,
            Subject = $"harvest:capture:{input.CaptureId:D}",
            Version = version,
            Models = [route],
            Variables = new Dictionary<string, string?> { ["description"] = description },
            // The shared cache only validates schema. Store and reuse the evidence-checked result in SCFD.
            Fresh = true,
        }, cancellationToken), DescribeCall);
        if (response is not Result<AiRunModel>.Success { Value: var run })
            return Failure(FetchOutcome.CapabilityUnavailable, "The pinned rental-terms prompt or model is unavailable.", retryable: false);
        if (run.Outcome != AiOutcome.Succeeded)
            return Failure(FailedOutcome(run), CallFailure(run), run, retryable: false);

        var validated = await input.Trace.RunOperationAsync("validate-result", () => Task.FromResult(
            IsPinned(run, route, version, promptHash)
                ? RentalTermEvidence.Parse(run.Output, input.Text, description.Length)
                : (null, "Inference metadata did not match the pinned prompt and route.")),
            result => result.Item1 is null ? new("failed", Input: 1, Message: result.Item2) : new(Input: 1, Output: 1));
        return validated.Item1 is { } answer
            ? Complete(input, answer, truncated, run)
            : Failure(FetchOutcome.UnexpectedContent, validated.Item2!, run, retryable: false);
    }

    private AppError? ValidateInput(CaptureProcessInput input)
    {
        if (!input.Source.AiAllowed)
            return AppErrorFactory.Forbidden("Model processing is disabled for this source.");
        if (!Serves(input.Source) || ExplicitNonRental(input))
            return AppErrorFactory.Validation("The rental-terms processor requires an OLX rental reading.");
        return ConfigurationError(input.Model, input.PromptVersion);
    }

    private AppError? ConfigurationError(string? route, int? version)
    {
        if (version is not > 0)
            return AppErrorFactory.Validation("Set a positive rental-terms prompt version.");
        if (!IsModelReference(route))
            return AppErrorFactory.Validation("Use a rental-terms model route in provider/model format.");
        if (!options.Value.Models.Contains(route!, StringComparer.Ordinal))
            return AppErrorFactory.Validation("Select a configured rental-terms model route.");
        return services.GetService<IAiRunner>() is null || services.GetService<IPromptsBroker>() is null
            ? AppErrorFactory.Conflict("The shared AI runner or prompt registry is unavailable for rental terms.")
            : null;
    }

    private async Task<string?> ResolvePromptHashAsync(string route, int version, CancellationToken cancellationToken)
    {
        var prompts = services.GetRequiredService<IPromptsBroker>();
        if (await prompts.GetUseCaseAsync(RentalTermsDefinition.UseCase, cancellationToken) is not
                Result<UseCaseModel>.Success { Value: { PromptMode: PromptMode.Versioned, KeepOutputs: false } useCase }
            || !SchemaMatches(useCase.OutputSchema))
            return null;

        int separator = route.IndexOf('/');
        if (await prompts.ResolveAsync(RentalTermsDefinition.UseCase, route[(separator + 1)..], version,
                cancellationToken: cancellationToken) is Result<PromptModel>.Success { Value: var prompt }
            && prompt.Version == version && prompt.UseCase == RentalTermsDefinition.UseCase
            && !string.IsNullOrWhiteSpace(prompt.ContentHash))
            return prompt.ContentHash;
        return null;
    }

    private static bool SchemaMatches(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
            return false;
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(schema), JsonNode.Parse(RentalTermsDefinition.OutputSchema));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsModelReference(string? route)
    {
        if (string.IsNullOrEmpty(route) || route.Any(char.IsWhiteSpace))
            return false;
        int separator = route.IndexOf('/');
        return separator > 0 && separator < route.Length - 1;
    }

    private static bool ExplicitNonRental(CaptureProcessInput input)
    {
        if (!input.Extras.TryGetValue("listing", out var raw) || ListingProjection.Read(raw) is not { } listing)
            return false;
        return listing.Offers.Any(offer => offer.Transaction is not null)
            && listing.Offers.All(offer => offer.Transaction != ListingTransaction.Rent);
    }

    private static bool IsPinned(AiRunModel run, string route, int version, string promptHash) =>
        run.Attempts is { Count: > 0 }
        && run.Attempts.All(attempt => attempt.UseCase == RentalTermsDefinition.UseCase && attempt.PromptVersion == version
            && attempt.PromptHash == promptHash
            && string.Equals(route, $"{attempt.Provider}/{attempt.Model}", StringComparison.Ordinal))
        && run.Final.Outcome == AiOutcome.Succeeded;

    private static StepOperationResult DescribeCall(Result<AiRunModel> result) =>
        result is Result<AiRunModel>.Success { Value: var run }
            ? run.Outcome == AiOutcome.Succeeded ? new(Input: 1, Output: 1)
                : new("failed", Input: 1, Output: 0, Message: CallFailure(run))
            : new("failed", Input: 1, Output: 0, Message: "The pinned rental-terms prompt or model is unavailable.");

    private static string CallFailure(AiRunModel run) => run.Outcome switch
    {
        AiOutcome.SchemaInvalid => "The model answer did not match the rental-terms schema.",
        AiOutcome.Refused => "The provider refused rental-terms processing.",
        _ => run.Attempts.LastOrDefault()?.ErrorKind switch
        {
            AiErrorKind.NotConfigured or AiErrorKind.Unauthorized => "The configured rental-terms provider is unavailable.",
            AiErrorKind.RateLimited => "The provider rate-limited rental-terms processing.",
            AiErrorKind.Timeout => "The rental-terms model request timed out.",
            _ => "The provider could not complete rental-terms processing.",
        },
    };

    private static FetchOutcome FailedOutcome(AiRunModel run) => run.Outcome switch
    {
        AiOutcome.SchemaInvalid => FetchOutcome.UnexpectedContent,
        _ => run.Attempts.LastOrDefault()?.ErrorKind switch
        {
            AiErrorKind.NotConfigured or AiErrorKind.Unauthorized => FetchOutcome.CapabilityUnavailable,
            AiErrorKind.Timeout => FetchOutcome.FetchTimeout,
            _ => FetchOutcome.ProviderFailed,
        },
    };

    private static CaptureProcessResult Failure(FetchOutcome outcome, string message, AiRunModel? run = null, bool retryable = true)
    {
        if (run?.Attempts.LastOrDefault()?.Id is { } inferenceId && inferenceId != Guid.Empty)
            message += $" AI inference {inferenceId:D}.";
        return new(new StepResult { Outcome = outcome, Message = message, Usage = Usage(run) }, Retryable: retryable);
    }

    private CaptureProcessResult Complete(CaptureProcessInput input, RentalTermsAnswer answer, bool truncated, AiRunModel? run)
    {
        var payload = new
        {
            schema = Key,
            fields = new[]
            {
                Field("deposit", "Deposit", answer.Deposit),
                Field("minimumStay", "Minimum stay", answer.MinimumStay),
                Field("advanceRent", "Advance rent", answer.AdvanceRent),
                Field("utilities", "Utilities", answer.Utilities),
            },
            modelRoute = input.Model,
            promptVersion = input.PromptVersion,
            inputTruncated = truncated,
            fromCache = run?.FromCache ?? false,
            inferences = run?.Attempts.Select(attempt => new
            {
                attempt.Id, attempt.AtUtc, attempt.PromptVersion, attempt.PromptHash, attempt.Provider, attempt.Model,
                attempt.InputHash, attempt.Attempt, attempt.InputTokens, attempt.OutputTokens, attempt.CacheReadTokens,
                attempt.CacheWriteTokens, attempt.ReasoningTokens, attempt.CostMicroUsd, attempt.Priced, attempt.LatencyMs,
                attempt.Outcome, attempt.ErrorKind,
            }).ToArray() ?? [],
        };
        return new(new StepResult { Outcome = FetchOutcome.Ok, Usage = Usage(run) },
            JsonSerializer.SerializeToElement(payload, RentalTermsDefinition.Wire));
    }

    private static StepUsage Usage(AiRunModel? run) => new()
    {
        EstimatedUsd = run is { FromCache: false } ? run.CostMicroUsd / 1_000_000m : 0,
    };

    private static FieldResult Field<T>(string key, string label, RentalTerm<T> term) where T : class =>
        new(key, label, term.Status, term.Claims.Select(claim => new ClaimResult(claim.Evidence,
            claim.Value is null ? null : JsonSerializer.SerializeToElement(claim.Value, RentalTermsDefinition.Wire))).ToArray());

    private sealed record FieldResult(string Key, string Label, RentalTermStatus Status, IReadOnlyList<ClaimResult> Claims);
    private sealed record ClaimResult(string Evidence, JsonElement? Value);
}
