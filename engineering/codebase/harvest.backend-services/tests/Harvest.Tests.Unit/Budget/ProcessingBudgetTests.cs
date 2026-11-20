using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Inferences;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

namespace Harvest.Tests.Unit.Budget;

/// <summary>The spend guard's reading service over a fake inference log: the window it sums, how long it reuses a reading,
/// and what it does when the log cannot be read.</summary>
public sealed class ProcessingBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Read_ShouldSumEveryUseCase_FromTheWindowStartToOneSecondPastNow()
    {
        var log = new FakeLog(
            Totals("extract-listing", "anthropic", "claude-sonnet-5-5", attempts: 2, cost: 3_000_000),
            Totals("post-tags", "anthropic", "claude-haiku-5-5", attempts: 40, cost: 1_500_000));
        using var host = Build(log, options => options.LimitUsd = 10m);

        var reading = await host.Budget.ReadAsync();

        reading.State.Should().Be(BudgetState.Open);
        reading.SpentMicroUsd.Should().Be(4_500_000);
        reading.ReadAtUtc.Should().Be(Start);
        reading.Rows.Select(row => (row.UseCase, row.Model, row.Attempts, row.CostMicroUsd)).Should().Equal(
            ("extract-listing", "anthropic/claude-sonnet-5-5", 2L, 3_000_000L), ("post-tags", "anthropic/claude-haiku-5-5", 40L, 1_500_000L));
        var query = log.Queries.Should().ContainSingle().Subject;
        query.FromUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        query.ToUtc.Should().Be(Start.AddSeconds(1));
        query.Scopes.Should().BeNullOrEmpty("every data scope counts");
    }

    [Fact]
    public async Task Read_ShouldReuseAReading_ForTheRefreshInterval()
    {
        var log = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 1, 1_000));
        using var host = Build(log, options => options.LimitUsd = 10m);

        (await host.Budget.ReadAsync()).SpentMicroUsd.Should().Be(1_000);
        log.Totals = [Totals("post-tags", "anthropic", "claude-haiku-5-5", 2, 9_000_000)];
        host.Clock.Advance(TimeSpan.FromSeconds(59));
        (await host.Budget.ReadAsync()).SpentMicroUsd.Should().Be(1_000, "a poll inside the interval reads memory");
        log.Queries.Should().HaveCount(1);

        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var next = await host.Budget.ReadAsync();
        next.SpentMicroUsd.Should().Be(9_000_000);
        next.State.Should().Be(BudgetState.Soft);
        log.Queries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Read_ShouldShareOneRefresh_BetweenWorkersThatPollTogether()
    {
        var log = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 1, 1_000));
        using var host = Build(log, options => options.LimitUsd = 10m);

        var readings = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => host.Budget.ReadAsync()));

        readings.Should().OnlyContain(reading => reading.SpentMicroUsd == 1_000);
        log.Queries.Should().HaveCount(1);
    }

    [Fact]
    public async Task Read_ShouldMoveToTheNextWindow_WhenTheMonthTurns()
    {
        var log = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 9, 12_000_000));
        using var host = Build(log, options => options.LimitUsd = 10m);
        (await host.Budget.ReadAsync()).State.Should().Be(BudgetState.Hard);

        host.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 0, 5, TimeSpan.Zero));
        log.Totals = [];
        var december = await host.Budget.ReadAsync();

        december.State.Should().Be(BudgetState.Open);
        december.WindowStartUtc.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        log.Queries[^1].FromUtc.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Read_ShouldCountFromSinceUtc_AndAskNothingOfALogThatHasNotStartedYet()
    {
        var log = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 1, 2_500_000));
        using var oneOff = Build(log, options => { options.LimitUsd = 10m; options.SinceUtc = Start.AddDays(-12); });

        var reading = await oneOff.Budget.ReadAsync();

        reading.WindowStartUtc.Should().Be(Start.AddDays(-12));
        reading.WindowEndUtc.Should().BeNull();
        reading.ProjectedMicroUsd.Should().BeNull();
        log.Queries.Should().ContainSingle().Which.FromUtc.Should().Be(Start.AddDays(-12));

        var later = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 1, 2_500_000));
        using var future = Build(later, options => { options.LimitUsd = 10m; options.SinceUtc = Start.AddDays(5); });
        var notYet = await future.Budget.ReadAsync();
        notYet.State.Should().Be(BudgetState.Open);
        notYet.SpentMicroUsd.Should().Be(0);
        later.Queries.Should().BeEmpty("the log accepts no period that ends before it starts");
    }

    [Fact]
    public async Task Read_ShouldBeOff_WithoutALimit_AndStillShowTheSpend()
    {
        var log = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 1, 700));
        using var host = Build(log);

        var reading = await host.Budget.ReadAsync();

        reading.State.Should().Be(BudgetState.Off);
        reading.LimitMicroUsd.Should().BeNull();
        reading.SpentMicroUsd.Should().Be(700);
    }

    [Fact]
    public async Task Read_ShouldBeOff_WhenTheHostComposesNoInferenceLog()
    {
        using var host = Build(log: null, options => options.LimitUsd = 10m);

        var reading = await host.Budget.ReadAsync();

        reading.State.Should().Be(BudgetState.Off);
        reading.SpentMicroUsd.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_ShouldKeepTheLastGoodReading_WhenARefreshFails(bool throws)
    {
        var log = new FakeLog(Totals("post-tags", "anthropic", "claude-haiku-5-5", 3, 8_500_000));
        using var host = Build(log, options => options.LimitUsd = 10m);
        var good = await host.Budget.ReadAsync();
        good.State.Should().Be(BudgetState.Soft);

        log.Break(throws);
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        var kept = await host.Budget.ReadAsync();

        kept.Should().BeSameAs(good);
        kept.ReadAtUtc.Should().Be(Start, "the reading keeps its own time");
        log.Queries.Should().HaveCount(2);
        (await host.Budget.ReadAsync()).Should().BeSameAs(good);
        log.Queries.Should().HaveCount(2, "a failed read is retried after the interval, not on every poll");

        log.Mend();
        log.Totals = [Totals("post-tags", "anthropic", "claude-haiku-5-5", 4, 10_500_000)];
        host.Clock.Advance(TimeSpan.FromMinutes(2));
        (await host.Budget.ReadAsync()).State.Should().Be(BudgetState.Hard);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_ShouldFailClosedAtHard_WhenTheLogCannotBeReadAndNothingWasRead(bool throws)
    {
        var log = new FakeLog();
        log.Break(throws);
        using var host = Build(log, options => options.LimitUsd = 10m);

        var reading = await host.Budget.ReadAsync();

        reading.State.Should().Be(BudgetState.Hard);
        reading.ReadFailed.Should().BeTrue();
        reading.RefusalMessage().Should().Contain("could not be read");

        log.Mend();
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var recovered = await host.Budget.ReadAsync();
        recovered.State.Should().Be(BudgetState.Open);
        recovered.ReadFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Read_ShouldStayOff_WhenTheLogCannotBeReadAndNoLimitIsSet()
    {
        var log = new FakeLog();
        log.Break(throws: true);
        using var host = Build(log);

        var reading = await host.Budget.ReadAsync();

        reading.State.Should().Be(BudgetState.Off);
        reading.ReadFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Read_ShouldNotSwallowACancelledCall()
    {
        var log = new FakeLog();
        log.Cancel();
        using var host = Build(log, options => options.LimitUsd = 10m);

        await FluentActions.Awaiting(() => host.Budget.ReadAsync()).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1_000_000_001)]
    public void Options_ShouldRefuseALimitOutsideItsRange(decimal limit) =>
        Failures(options => options.LimitUsd = limit).Should().ContainSingle().Which.Should().Contain("LimitUsd");

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    public void Options_ShouldRefuseASoftShareOutsideZeroToOne(double share) =>
        Failures(options => options.SoftShare = (decimal)share).Should().ContainSingle().Which.Should().Contain("SoftShare");

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(-1)]
    public void Options_ShouldRefuseACycleStartDayOutsideOneToTwentyEight(int day) =>
        Failures(options => options.CycleStartDay = day).Should().ContainSingle().Which.Should().Contain("CycleStartDay");

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:05")]
    [InlineData("01:00:01")]
    public void Options_ShouldRefuseARefreshIntervalOfNoTimeOrOverAnHour(string interval) =>
        Failures(options => options.RefreshInterval = TimeSpan.Parse(interval)).Should().ContainSingle().Which.Should().Contain("RefreshInterval");

    [Fact]
    public void Options_ShouldAcceptTheBoundsAndTheDefaults()
    {
        Failures(_ => { }).Should().BeEmpty();
        Failures(options => { options.LimitUsd = 200m; options.SoftShare = 1m; options.CycleStartDay = 28; options.RefreshInterval = TimeSpan.FromHours(1); })
            .Should().BeEmpty();
        Failures(options => options.SinceUtc = Start).Should().BeEmpty();
    }

    [Fact]
    public void Registration_ShouldAddTheRulesOnce_WhateverHowManyPassesRegisterTheGuard()
    {
        var services = new ServiceCollection();
        services.AddHarvestProcessingBudget();
        services.AddHarvestProcessingBudget();
        services.AddHarvestProcessingBudget().Configure(options => options.CycleStartDay = 40);
        using var provider = services.BuildServiceProvider();

        FluentActions.Invoking(() => provider.GetRequiredService<IOptions<ProcessingBudgetOptions>>().Value)
            .Should().Throw<OptionsValidationException>().Which.Failures.Should().ContainSingle();
        services.Count(descriptor => descriptor.ServiceType == typeof(IProcessingBudget)).Should().Be(1);
    }

    private static IReadOnlyCollection<string> Failures(Action<ProcessingBudgetOptions> change)
    {
        var services = new ServiceCollection();
        services.AddHarvestProcessingBudget().Configure(change);
        using var provider = services.BuildServiceProvider();
        try
        {
            _ = provider.GetRequiredService<IOptions<ProcessingBudgetOptions>>().Value;
            return [];
        }
        catch (OptionsValidationException invalid)
        {
            return [.. invalid.Failures];
        }
    }

    private static InferenceTotals Totals(string useCase, string provider, string model, long attempts, long cost) => new()
    {
        UseCase = useCase, Provider = provider, Model = model, Attempts = attempts, CostMicroUsd = cost,
    };

    private static Host Build(FakeLog? log = null, Action<ProcessingBudgetOptions>? configure = null)
    {
        var clock = new TestClock(Start);
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        if (log is not null)
            services.AddScoped<IInferencesBroker>(_ => log);
        services.AddHarvestProcessingBudget().Configure(options => configure?.Invoke(options));
        var provider = services.BuildServiceProvider(validateScopes: true);
        return new Host(provider, clock);
    }

    private sealed class Host(ServiceProvider provider, TestClock clock) : IDisposable
    {
        internal IProcessingBudget Budget { get; } = provider.GetRequiredService<IProcessingBudget>();
        internal TestClock Clock { get; } = clock;

        public void Dispose() => provider.Dispose();
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        internal void Advance(TimeSpan by) => current += by;

        internal void SetUtcNow(DateTimeOffset value) => current = value;
    }

    private sealed class FakeLog(params InferenceTotals[] totals) : IInferencesBroker
    {
        private readonly List<InferenceTotalsQuery> queries = [];
        private Exception? error;
        private bool refuse;

        internal IReadOnlyList<InferenceTotals> Totals { get; set; } = totals;

        internal IReadOnlyList<InferenceTotalsQuery> Queries => queries;

        /// <summary>Makes every read fail: with an exception, or with a failed result.</summary>
        internal void Break(bool throws)
        {
            error = throws ? new InvalidOperationException("The database is down.") : null;
            refuse = !throws;
        }

        /// <summary>Makes every read throw a cancellation.</summary>
        internal void Cancel() => error = new OperationCanceledException();

        /// <summary>Makes reads answer again.</summary>
        internal void Mend()
        {
            error = null;
            refuse = false;
        }

        public Task<Result<IReadOnlyList<InferenceTotals>>> TotalsAsync(InferenceTotalsQuery query, CancellationToken cancellationToken = default)
        {
            queries.Add(query);
            if (error is not null)
                return Task.FromException<Result<IReadOnlyList<InferenceTotals>>>(error);
            return Task.FromResult(refuse
                ? Result<IReadOnlyList<InferenceTotals>>.Fail(AppErrorFactory.Conflict("The log is unavailable."))
                : Result<IReadOnlyList<InferenceTotals>>.Ok(Totals));
        }

        public Task<Result<IReadOnlyList<InferenceModel>>> ListAsync(InferenceQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<int>> ForgetSubjectAsync(string subject, IReadOnlyList<string>? scopes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
