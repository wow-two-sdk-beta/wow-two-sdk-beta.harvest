using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Ai.Inferences;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestAdapters.Tests.Unit.Marketplace;

public sealed class RentalTermsV2Tests
{
    private const string Route = "fixture/lease-model";
    private const int PromptVersion = 17;
    private const int DescriptionLimit = 12_000;

    [Fact]
    public void OutputSchema_ShouldRequireEveryTypedSlotAndDisallowUnexpectedProperties()
    {
        using var schema = JsonDocument.Parse(RentalTermsDefinition.OutputSchema);
        schema.RootElement.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        schema.RootElement.GetProperty("required").EnumerateArray().Select(name => name.GetString()).Should()
            .BeEquivalentTo("deposit", "minimumStay", "advanceRent", "utilities");
    }

    [Fact]
    public async Task Process_ShouldStoreTypedMultilingualEvidenceAndInferenceMetadata()
    {
        const string description = "Депозит: 500 USD. Kamida 3 tun. Олдиндан тўлов 2 ой. Коммунальные включены.";
        var model = new ScriptedRunner(Answer(
            deposit: Term("present", Claim("Депозит: 500 USD", Money(500m, "USD"))),
            minimumStay: Term("present", Claim("Kamida 3 tun", new { count = 3, unit = "night" })),
            advanceRent: Term("present", Claim("Олдиндан тўлов 2 ой",
                new { amount = (decimal?)null, currency = (string?)null, rentMultiple = (decimal?)null, duration = new { count = 2, unit = "month" } })),
            utilities: Term("present", Claim("Коммунальные включены", new { billing = "included" }))));
        var input = Input(description);
        var prompts = new ScriptedPrompts();
        var result = await Process(model, input, prompts);

        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        result.StepResult.Usage.EstimatedUsd.Should().Be(0.000123m);
        var output = result.Output!.Value;
        output.GetProperty("schema").GetString().Should().Be("rental-terms@2");
        output.GetProperty("promptVersion").GetInt32().Should().Be(PromptVersion);
        output.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("key").GetString()).Should()
            .Equal("deposit", "minimumStay", "advanceRent", "utilities");
        Value(output, "deposit").GetProperty("amount").GetDecimal().Should().Be(500m);
        Value(output, "minimumStay").GetProperty("unit").GetString().Should().Be("night");
        Value(output, "advanceRent").GetProperty("duration").GetProperty("unit").GetString().Should().Be("month");
        Value(output, "utilities").GetProperty("billing").GetString().Should().Be("included");
        var inference = output.GetProperty("inferences")[0];
        inference.GetProperty("promptHash").GetString().Should().Be("prompt-hash");
        inference.GetProperty("outputTokens").GetInt64().Should().Be(21);
        inference.GetProperty("priced").GetBoolean().Should().BeTrue();
        inference.TryGetProperty("output", out _).Should().BeFalse();
        inference.TryGetProperty("error", out _).Should().BeFalse();
        model.Requests.Should().ContainSingle();
        var request = model.Requests[0];
        request.UseCase.Should().Be("olx-rental-terms");
        request.Version.Should().Be(PromptVersion);
        request.Models.Should().Equal(Route);
        request.Subject.Should().Be($"harvest:capture:{input.CaptureId:D}");
        request.Variables.Keys.Should().Equal("description");
        request.Variables["description"].Should().Be(description);
        prompts.Resolutions.Should().Equal(("lease-model", PromptVersion));
    }

    [Theory]
    [InlineData("Депозит 500 USD", "Депозит 500 USD", 600, "USD")]
    [InlineData("Депозит 500 USD", "Депозит 500 USD", 500, "UZS")]
    [InlineData("Нет депозита 500 USD", "депозита 500 USD", 500, "USD")]
    [InlineData("Депозит 500 USD не требуется", "Депозит 500 USD", 500, "USD")]
    [InlineData("Депозит обсуждается при аренде 500 USD", "Депозит обсуждается при аренде 500 USD", 500, "USD")]
    [InlineData("Возможно, депозит 500 USD", "депозит 500 USD", 500, "USD")]
    [InlineData("Депозит 500. Оплата аренды в USD", "Депозит 500. Оплата аренды в USD", 500, "USD")]
    [InlineData("Депозит 500 тыс. сум", "Депозит 500 тыс. сум", 500, "UZS")]
    [InlineData("Залог 500 USD", "Депозит 500 USD", 500, "USD")]
    [InlineData("Депозит 500 у.е.", "Депозит 500 у.е.", 500, "USD")]
    [InlineData("Deposit 1500 USD", "Deposit 15", 15, null)]
    [InlineData("nondeposit 200 USD", "deposit 200 USD", 200, "USD")]
    [InlineData("Deposit 1 500", "Deposit 1", 1, null)]
    [InlineData("Deposit 1500 USD", "Deposit 1500", 1500, null)]
    [InlineData("Deposit USD 1,500", "Deposit USD 1", 1, "USD")]
    public async Task Process_ShouldRejectInconsistentOrInventedMonetaryClaims(
        string description, string evidence, int amount, string? currency)
    {
        var model = new ScriptedRunner(Answer(deposit: Term("present", Claim(evidence, Money(amount, currency)))));
        var result = await Process(model, Input(description));
        result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.Output.Should().BeNull();
        result.Retryable.Should().BeFalse();
        result.StepResult.Usage.EstimatedUsd.Should().Be(0.000123m);
        result.StepResult.Message.Should().Contain(model.InferenceId.ToString("D")).And.NotContain("not exposed");
    }

    [Fact]
    public async Task Process_ShouldRejectAnAmountCutOffAtTheDescriptionLimit()
    {
        const string quote = "Deposit 15";
        string prefix = new string('x', DescriptionLimit - quote.Length - 1) + "\n" + quote;
        var model = new ScriptedRunner(Answer(deposit: Term("present", Claim(quote, Money(15, null)))));
        var result = await Process(model, Input(prefix + "00 USD."));

        model.Requests.Should().ContainSingle().Which.Variables["description"].Should().Be(prefix);
        result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.Output.Should().BeNull();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public async Task Process_ShouldKeepValidEvidenceInTheVisiblePrefixOfALongDescription()
    {
        const string quote = "Deposit 1500 USD";
        string description = quote + ".\n" + new string('x', DescriptionLimit);
        var model = new ScriptedRunner(Answer(deposit: Term("present", Claim(quote, Money(1500, "USD")))));
        var result = await Process(model, Input(description));

        model.Requests.Should().ContainSingle().Which.Variables["description"].Should().Be(description[..DescriptionLimit]);
        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        var output = result.Output!.Value;
        output.GetProperty("inputTruncated").GetBoolean().Should().BeTrue();
        Value(output, "deposit").GetProperty("amount").GetDecimal().Should().Be(1500);
    }

    [Fact]
    public async Task Process_ShouldRejectEvidenceWhollyOutsideTheVisibleDescription()
    {
        const string quote = "Deposit 1500 USD";
        string prefix = new string('x', DescriptionLimit);
        var model = new ScriptedRunner(Answer(deposit: Term("present", Claim(quote, Money(1500, "USD")))));
        var result = await Process(model, Input(prefix + "\n" + quote + "."));

        model.Requests.Should().ContainSingle().Which.Variables["description"].Should().Be(prefix);
        result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.Output.Should().BeNull();
    }

    [Theory]
    [InlineData("Депозит 500 тыс. сум", 500_000)]
    [InlineData("Garov puli 1 500 000 so‘m", 1_500_000)]
    public async Task Process_ShouldNormalizeExplicitThousands(string quote, int amount)
    {
        var model = new ScriptedRunner(Answer(deposit: Term("present", Claim(quote, Money(amount, "UZS")))));
        var result = await Process(model, Input(quote + "."));
        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        Value(result.Output!.Value, "deposit").GetProperty("amount").GetDecimal().Should().Be(amount);
    }

    [Fact]
    public async Task Process_ShouldRejectBothReadingsOfAmbiguousMoneyGrouping()
    {
        const string text = "Deposit USD 1,500";
        foreach (decimal amount in new[] { 1.5m, 1500m })
        {
            var model = new ScriptedRunner(Answer(deposit: Term("present", Claim(text, Money(amount, "USD")))));
            var result = await Process(model, Input(text));
            result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        }
    }

    [Theory]
    [InlineData("Залог обязателен", "Залог обязателен", true)]
    [InlineData("Garov puli shart", "Garov puli shart", true)]
    [InlineData("Залог обсуждается", "Залог обсуждается", false)]
    [InlineData("Залог обязателен только при посуточной аренде", "Залог обязателен", false)]
    [InlineData("Залог обязателен 1500 USD", "Залог обязателен", false)]
    public async Task Process_ShouldDistinguishAnUnspecifiedDepositAmountFromAnAmbiguousMention(
        string description, string evidence, bool valid)
    {
        var model = new ScriptedRunner(Answer(deposit: Term("present",
            Claim(evidence, new { amount = (decimal?)null, currency = (string?)null, rentMultiple = (decimal?)null }))));
        var result = await Process(model, Input(description));
        result.StepResult.Outcome.Should().Be(valid ? FetchOutcome.Ok : FetchOutcome.UnexpectedContent);
        if (valid)
        {
            Field(result.Output!.Value, "deposit").GetProperty("status").GetString().Should().Be("present");
            Value(result.Output.Value, "deposit").GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Null);
        }
        else
            result.Output.Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldKeepAdvanceRequiredWithoutInferringAmountOrDuration()
    {
        const string text = "Предоплата обязательна.";
        var model = new ScriptedRunner(Answer(advanceRent: Term("present", Claim("Предоплата обязательна",
            new { amount = (decimal?)null, currency = (string?)null, rentMultiple = (decimal?)null, duration = (object?)null }))));
        var result = await Process(model, Input(text));
        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        Value(result.Output!.Value, "advanceRent").GetProperty("duration").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("От 2 ночей", 2, "night", null)]
    [InlineData("kamida 3 oyga", 3, "month", null)]
    [InlineData("депозит 1 500 000 сўм, камида 6 ойга", 6, "month", "камида 6 ойга")]
    public async Task Process_ShouldRecognizeExplicitMinimumForms(string description, int count, string unit, string? evidence)
    {
        var model = new ScriptedRunner(Answer(minimumStay: Term("present",
            Claim(evidence ?? description, new { count, unit }))));
        var result = await Process(model, Input(description));
        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        Value(result.Output!.Value, "minimumStay").GetProperty("unit").GetString().Should().Be(unit);
    }

    [Fact]
    public async Task Process_ShouldRejectOneEndOfAStatedRangeAsAnExactMinimum()
    {
        const string text = "Минимум 3-5 ночей.";
        var model = new ScriptedRunner(Answer(
            minimumStay: Term("present", Claim("Минимум 3-5 ночей", new { count = 5, unit = "night" }))));
        var result = await Process(model, Input(text));
        result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldTreatExcludingUtilitiesAsDifferentFromWaivingCharges()
    {
        const string text = "Аренда без коммунальных платежей.";
        var model = new ScriptedRunner(Answer(utilities: Term("absent", Claim("без коммунальных платежей", null))));
        var result = await Process(model, Input(text));
        result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.Output.Should().BeNull();
    }

    [Theory]
    [InlineData("Utilities included in the deposit", "Utilities included", false)]
    [InlineData("In the deposit utilities are included", "utilities are included", false)]
    [InlineData("Utilities included, except water", "Utilities included", false)]
    [InlineData("Utilities included in the monthly rent", "Utilities included", true)]
    public async Task Process_ShouldCheckWhereUtilitiesAreIncludedEvenWhenTheQuoteIsClipped(string description, string evidence, bool valid)
    {
        var model = new ScriptedRunner(Answer(utilities: Term("present", Claim(evidence, new { billing = "included" }))));
        var result = await Process(model, Input(description));
        result.StepResult.Outcome.Should().Be(valid ? FetchOutcome.Ok : FetchOutcome.UnexpectedContent);
    }

    [Fact]
    public async Task Process_ShouldCheckTheUtilityQualifierAfterTheVisibleDescription()
    {
        const string quote = "Utilities included";
        string prefix = new string('x', DescriptionLimit - quote.Length - 1) + "\n" + quote;
        var model = new ScriptedRunner(Answer(utilities: Term("present", Claim(quote, new { billing = "included" }))));
        var result = await Process(model, Input(prefix + " in the deposit."));

        model.Requests.Should().ContainSingle().Which.Variables["description"].Should().Be(prefix);
        result.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        result.Output.Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldRetainConflictingClaimsAndUnknownFields()
    {
        const string description = "Depozit yo'q. Депозит в размере месячной аренды. Коммунальные отдельно.";
        var model = new ScriptedRunner(Answer(
            deposit: Term("conflict", Claim("Depozit yo'q", null),
                Claim("Депозит в размере месячной аренды", new { amount = (decimal?)null, currency = (string?)null, rentMultiple = 1m })),
            utilities: Term("present", Claim("Коммунальные отдельно", new { billing = "separate" }))));
        var result = await Process(model, Input(description));

        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        var output = result.Output!.Value;
        var deposit = Field(output, "deposit");
        deposit.GetProperty("status").GetString().Should().Be("conflict");
        deposit.GetProperty("claims")[0].GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        deposit.GetProperty("claims")[1].GetProperty("value").GetProperty("rentMultiple").GetDecimal().Should().Be(1m);
        Field(output, "minimumStay").GetProperty("status").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task Process_ShouldDistinguishExplicitAbsenceFromInvalidOrEmptyContent()
    {
        const string text = "Залог не нужен.";
        var waived = await Process(new ScriptedRunner(Answer(deposit: Term("absent", Claim("Залог не нужен", null)))), Input(text));
        var invalid = await Process(new ScriptedRunner("{}"), Input(text));
        waived.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        Field(waived.Output!.Value, "deposit").GetProperty("status").GetString().Should().Be("absent");
        invalid.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        invalid.Output.Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldRecognizeAnExplicitUzbekDepositWaiver()
    {
        const string text = "Depozit olinmaydi.";
        var model = new ScriptedRunner(Answer(deposit: Term("absent", Claim("Depozit olinmaydi", null))));
        var result = await Process(model, Input(text));
        result.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        Field(result.Output!.Value, "deposit").GetProperty("status").GetString().Should().Be("absent");
    }

    [Fact]
    public async Task Process_ShouldSkipBlankDescriptionAndRejectUnpinnedInference()
    {
        var unused = new ScriptedRunner(Answer());
        var blank = await Process(unused, Input(" "));
        blank.StepResult.Outcome.Should().Be(FetchOutcome.Ok);
        unused.Requests.Should().BeEmpty();
        blank.Output!.Value.GetProperty("inferences").GetArrayLength().Should().Be(0);
        Field(blank.Output.Value, "deposit").GetProperty("status").GetString().Should().Be("unknown");

        var unpinned = await Process(new ScriptedRunner(Answer(), promptVersion: PromptVersion + 1), Input("Toshkent."));
        unpinned.StepResult.Outcome.Should().Be(FetchOutcome.UnexpectedContent);
        unpinned.Output.Should().BeNull();
    }

    [Fact]
    public async Task Process_ShouldRejectAnOlderResolvedPromptBeforeCallingTheModel()
    {
        var model = new ScriptedRunner(Answer());
        var result = await Process(model, Input("Toshkent."), new ScriptedPrompts(PromptVersion - 1));
        result.StepResult.Outcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        result.Output.Should().BeNull();
        result.Retryable.Should().BeTrue();
        model.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PromptVersion - 1, false)]
    [InlineData(PromptVersion, true)]
    public async Task ValidateConfigurationAsync_ShouldRequireTheExactPromptWithoutCallingTheModel(int resolvedVersion, bool available)
    {
        var model = new ScriptedRunner(Answer());
        var prompts = new ScriptedPrompts(resolvedVersion);
        await using var host = BuildHost(model, prompts);
        await using var scope = host.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredKeyedService<ICaptureProcessor>(RentalTermsDefinition.ProcessorKey);

        var error = await processor.ValidateConfigurationAsync(new PipelineStageOptions
        {
            Model = Route, PromptVersion = PromptVersion,
        }, CancellationToken.None);

        (error is null).Should().Be(available);
        if (error is not null)
            error.Message.Should().Contain("exact rental-terms prompt");
        prompts.Resolutions.Should().Equal(("lease-model", PromptVersion));
        model.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Process_ShouldRejectAUseCaseThatKeepsModelAnswersBeforeCallingTheModel()
    {
        var model = new ScriptedRunner(Answer());
        var result = await Process(model, Input("Toshkent."), new ScriptedPrompts(keepOutputs: true));
        result.StepResult.Outcome.Should().Be(FetchOutcome.CapabilityUnavailable);
        model.Requests.Should().BeEmpty();
    }

    private static JsonElement Field(JsonElement output, string key) =>
        output.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("key").GetString() == key);

    private static JsonElement Value(JsonElement output, string key) => Field(output, key).GetProperty("claims")[0].GetProperty("value");

    private static async Task<CaptureProcessResult> Process(ScriptedRunner model, CaptureProcessInput input, ScriptedPrompts? prompts = null)
    {
        await using var host = BuildHost(model, prompts ?? new ScriptedPrompts());
        await using var scope = host.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredKeyedService<ICaptureProcessor>(RentalTermsDefinition.ProcessorKey).ProcessAsync(input);
    }

    private static ServiceProvider BuildHost(ScriptedRunner model, ScriptedPrompts prompts)
    {
        var services = new ServiceCollection();
        services.AddHarvestListingSources();
        services.Configure<RentalTermsOptions>(options => options.Models = [Route]);
        services.AddSingleton<IAiRunner>(model);
        services.AddSingleton<IPromptsBroker>(prompts);
        return services.BuildServiceProvider();
    }

    private static CaptureProcessInput Input(string text)
    {
        Guid container = Guid.NewGuid();
        var source = new SourceModel
        {
            Id = Guid.NewGuid(), ContainerId = container, Family = SourceFamily.Site, Handle = "www.olx.uz",
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
            Permission = SourcePermission.Open, AiAllowed = true, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
            ConsentBasis = default, Authority = 50, RetentionClass = "standard", CapabilityPolicy = new(),
            State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };
        return new(container, source, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, text, null,
            new Dictionary<string, JsonElement>(), Route, new DirectTrace(), PromptVersion);
    }

    private static string Answer(object? deposit = null, object? minimumStay = null, object? advanceRent = null, object? utilities = null) =>
        JsonSerializer.Serialize(new
        {
            deposit = deposit ?? Term("unknown"),
            minimumStay = minimumStay ?? Term("unknown"),
            advanceRent = advanceRent ?? Term("unknown"),
            utilities = utilities ?? Term("unknown"),
        });

    private static object Term(string status, params object[] claims) => new { status, claims };
    private static object Claim(string evidence, object? value) => new { evidence, value };
    private static object Money(decimal amount, string? currency) => new { amount, currency, rentMultiple = (decimal?)null };

    private sealed class DirectTrace : ICaptureProcessTrace
    {
        public Task<T> RunOperationAsync<T>(string key, Func<Task<T>> execute, Func<T, StepOperationResult>? describe = null) =>
            execute();
    }

    private sealed class ScriptedRunner(string output, int promptVersion = PromptVersion) : IAiRunner
    {
        internal Guid InferenceId { get; } = Guid.NewGuid();
        internal List<AiRunRequest> Requests { get; } = [];

        public Task<Result<AiRunModel>> RunAsync(AiRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result<AiRunModel>.Ok(new()
            {
                Outcome = AiOutcome.Succeeded,
                Output = output,
                Attempts = [new InferenceModel
                {
                    Id = InferenceId, AtUtc = DateTimeOffset.UnixEpoch, UseCase = RentalTermsDefinition.UseCase,
                    PromptVersion = promptVersion, PromptHash = "prompt-hash", Provider = "fixture", Model = "lease-model", Attempt = 1,
                    Outcome = AiOutcome.Succeeded, InputHash = "input-hash", InputTokens = 120, OutputTokens = 21,
                    CostMicroUsd = 123, Priced = true, LatencyMs = 170, Output = "not exposed",
                }],
            }));
        }
    }

    private sealed class ScriptedPrompts(int resolvedVersion = PromptVersion, bool keepOutputs = false) : IPromptsBroker
    {
        internal List<(string Model, int Version)> Resolutions { get; } = [];

        public Task<Result<UseCaseModel>> GetUseCaseAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<UseCaseModel>.Ok(new()
            {
                Key = key, PromptMode = PromptMode.Versioned, OutputSchema = RentalTermsDefinition.OutputSchema, KeepOutputs = keepOutputs,
            }));

        public Task<Result<PromptModel>> ResolveAsync(string useCase, string model, int? version = null, string? label = null,
            CancellationToken cancellationToken = default)
        {
            Resolutions.Add((model, version ?? 0));
            return Task.FromResult(Result<PromptModel>.Ok(new()
            {
                Id = Guid.NewGuid(), UseCase = useCase, Version = resolvedVersion, ContentHash = "prompt-hash",
                SystemTemplate = "fixture", UserTemplate = "{{description}}",
            }));
        }

        public Task<Result<UseCaseModel>> PutUseCaseAsync(UseCaseModel useCase, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<IReadOnlyList<UseCaseModel>>> ListUseCasesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<PromptModel>> SavePromptAsync(PromptDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<IReadOnlyList<PromptModel>>> ListPromptsAsync(string useCase, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<PromptLabelModel>> MoveLabelAsync(string useCase, string label, int version, string? by = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<PromptLabelModel>>> ListLabelsAsync(string useCase, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
