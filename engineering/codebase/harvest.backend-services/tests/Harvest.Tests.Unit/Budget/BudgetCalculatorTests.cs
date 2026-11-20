using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

namespace Harvest.Tests.Unit.Budget;

/// <summary>The spend guard's arithmetic: the state at a spend, the monthly and one-off windows, the projection and the
/// reading's wire form.</summary>
public sealed class BudgetCalculatorTests
{
    private static readonly DateTimeOffset Nov19 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, null, null, BudgetState.Off)]
    [InlineData(999_999_999, null, null, BudgetState.Off)]
    [InlineData(0, 10_000_000, 8_000_000, BudgetState.Open)]
    [InlineData(7_999_999, 10_000_000, 8_000_000, BudgetState.Open)]
    [InlineData(8_000_000, 10_000_000, 8_000_000, BudgetState.Soft)]
    [InlineData(9_999_999, 10_000_000, 8_000_000, BudgetState.Soft)]
    [InlineData(10_000_000, 10_000_000, 8_000_000, BudgetState.Hard)]
    [InlineData(12_000_000, 10_000_000, 8_000_000, BudgetState.Hard)]
    [InlineData(10_000_000, 10_000_000, 10_000_000, BudgetState.Hard)]
    public void Classify_ShouldTurnSoftAtTheSoftThreshold_AndHardAtTheLimit(int spent, int? limit, int? soft, BudgetState expected)
    {
        BudgetCalculator.Classify(spent, limit, soft).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, BudgetState.Open)]
    [InlineData(7_999_999, BudgetState.Open)]
    [InlineData(8_000_000, BudgetState.Soft)]
    [InlineData(9_999_999, BudgetState.Soft)]
    [InlineData(10_000_000, BudgetState.Hard)]
    public void Compose_ShouldDeriveTheThresholdsFromTheLimitAndTheSoftShare(long spent, BudgetState expected)
    {
        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 10m, SoftShare = 0.8m }, Nov19, Rows(spent));

        reading.State.Should().Be(expected);
        reading.LimitMicroUsd.Should().Be(10_000_000);
        reading.SoftMicroUsd.Should().Be(8_000_000);
        reading.SpentMicroUsd.Should().Be(spent);
    }

    [Fact]
    public void Compose_ShouldBeOffWithoutALimit_AndStillReportTheSpend()
    {
        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions(), Nov19, Rows(5_000_000));

        reading.State.Should().Be(BudgetState.Off);
        reading.LimitMicroUsd.Should().BeNull();
        reading.SoftMicroUsd.Should().BeNull();
        reading.SpentMicroUsd.Should().Be(5_000_000);
    }

    [Theory]
    [InlineData("2026-11-24T23:59:59Z", "2026-10-25T00:00:00Z", "2026-11-25T00:00:00Z")]
    [InlineData("2026-11-25T00:00:00Z", "2026-11-25T00:00:00Z", "2026-12-25T00:00:00Z")]
    [InlineData("2026-11-26T08:30:00Z", "2026-11-25T00:00:00Z", "2026-12-25T00:00:00Z")]
    [InlineData("2027-01-10T12:00:00Z", "2026-12-25T00:00:00Z", "2027-01-25T00:00:00Z")]
    public void Window_ShouldStartOnTheLatestCycleDay_AtMidnightUtc(string now, string start, string end)
    {
        var window = BudgetCalculator.Window(new ProcessingBudgetOptions { CycleStartDay = 25 }, DateTimeOffset.Parse(now));

        window.Start.Should().Be(DateTimeOffset.Parse(start));
        window.End.Should().Be(DateTimeOffset.Parse(end));
    }

    [Fact]
    public void Window_ShouldStartOnTheFirstOfTheMonth_ByDefault()
    {
        var december = BudgetCalculator.Window(new ProcessingBudgetOptions { CycleStartDay = 1 }, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        december.Start.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        december.End.Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var lastSecond = BudgetCalculator.Window(new ProcessingBudgetOptions(), new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
        lastSecond.Start.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        lastSecond.End.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Window_ShouldCountInUtc_WhateverTheOffsetOfTheClock()
    {
        // 2026-12-01 01:00 at +05:00 is still 2026-11-30 20:00 UTC.
        var window = BudgetCalculator.Window(new ProcessingBudgetOptions(), new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.FromHours(5)));

        window.Start.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Window_ShouldCountFromSinceUtcAndNeverReset_WhenItIsSet()
    {
        var since = new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero);
        var options = new ProcessingBudgetOptions { SinceUtc = since, CycleStartDay = 25 };

        foreach (var now in new[] { Nov19, new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero) })
        {
            var window = BudgetCalculator.Window(options, now);
            window.Start.Should().Be(since);
            window.End.Should().BeNull();
        }
        BudgetCalculator.Compose(options, Nov19, Rows(1)).WindowEndUtc.Should().BeNull();
    }

    [Fact]
    public void Compose_ShouldProjectTheMonth_FromTheSpendSoFar()
    {
        // 10 of November's 30 days have passed.
        var tenDays = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 100m }, tenDays, Rows(5_000_000));

        reading.ProjectedMicroUsd.Should().Be(15_000_000);
        reading.ProjectedUsd.Should().Be(15m);
        reading.WindowStartUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        reading.WindowEndUtc.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Projection_ShouldBeNoneForAOneOffWindow_AndAtTheWindowStart_AndZeroForNoSpend()
    {
        var month = new BudgetWindow(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        BudgetCalculator.Projection(5, new BudgetWindow(month.Start, null), Nov19).Should().BeNull();
        BudgetCalculator.Projection(5, month, month.Start).Should().BeNull("nothing has elapsed to extrapolate from");
        BudgetCalculator.Projection(0, month, Nov19).Should().Be(0);
        BudgetCalculator.Projection(long.MaxValue / 2, month, month.Start.AddTicks(1)).Should().Be(long.MaxValue, "it saturates");
    }

    [Fact]
    public void Compose_ShouldSumEveryRow()
    {
        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 1m }, Nov19,
        [
            new BudgetUsageRow { UseCase = "post-tags", Model = "anthropic/claude-haiku-5-5", Attempts = 3, CostMicroUsd = 120 },
            new BudgetUsageRow { UseCase = "extract-listing", Model = "anthropic/claude-sonnet-5-5", Attempts = 1, CostMicroUsd = 3_000 },
        ]);

        reading.SpentMicroUsd.Should().Be(3_120);
        reading.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Routes_ShouldKeepOnlyTheLastRoute_AtSoft()
    {
        string[] routes = ["anthropic/claude-sonnet-5-5", "anthropic/claude-haiku-5-5"];

        Reading(BudgetState.Soft).Routes(routes).Should().Equal("anthropic/claude-haiku-5-5");
        Reading(BudgetState.Soft).Routes(routes[..1]).Should().Equal(new[] { "anthropic/claude-sonnet-5-5" }, "one route is unchanged");
        Reading(BudgetState.Open).Routes(routes).Should().Equal(routes);
        Reading(BudgetState.Off).Routes(routes).Should().Equal(routes);
    }

    [Fact]
    public void RefusalMessage_ShouldNameTheSpendTheLimitAndTheSettingToRaise()
    {
        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 10m }, Nov19, Rows(10_234_500));

        reading.RefusalMessage().Should().Be("The AI spend limit for this window is reached (10.2345 of 10.0000 USD); "
            + "raise Harvest:Processing:Budget:LimitUsd or wait for the next window.");
        (reading with { ReadFailed = true }).RefusalMessage().Should().Contain("could not be read");
    }

    [Fact]
    public void Reading_ShouldWriteCamelCaseWithAmountsInMicroUsdAndInUsdWithFourPlaces()
    {
        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 10m, SoftShare = 0.5m },
            new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero), [new BudgetUsageRow
            {
                UseCase = "post-tags", Model = "anthropic/claude-haiku-5-5", Attempts = 4, CostMicroUsd = 5_000_000,
            }]);

        string json = JsonSerializer.Serialize(reading, JsonOptionsConstants.Default);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("state").GetString().Should().Be("soft");
        root.GetProperty("limitMicroUsd").GetInt64().Should().Be(10_000_000);
        root.GetProperty("softMicroUsd").GetInt64().Should().Be(5_000_000);
        root.GetProperty("spentMicroUsd").GetInt64().Should().Be(5_000_000);
        root.GetProperty("projectedMicroUsd").GetInt64().Should().Be(15_000_000);
        json.Should().Contain("\"spentUsd\":5.0000").And.Contain("\"limitUsd\":10.0000").And.Contain("\"softUsd\":5.0000")
            .And.Contain("\"projectedUsd\":15.0000");
        root.GetProperty("rows")[0].GetProperty("model").GetString().Should().Be("anthropic/claude-haiku-5-5");
        root.GetProperty("rows")[0].GetProperty("costMicroUsd").GetInt64().Should().Be(5_000_000);
        root.GetProperty("windowStartUtc").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Reading_ShouldLeaveOutTheEndAndTheProjection_OfAOneOffWindow()
    {
        var reading = BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 10m, SinceUtc = Nov19.AddDays(-3) }, Nov19, Rows(1));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(reading, JsonOptionsConstants.Default));
        document.RootElement.TryGetProperty("windowEndUtc", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("projectedMicroUsd", out _).Should().BeFalse();
        document.RootElement.TryGetProperty("projectedUsd", out _).Should().BeFalse();
    }

    private static BudgetUsageRow[] Rows(long cost) =>
        [new BudgetUsageRow { UseCase = "post-tags", Model = "anthropic/claude-haiku-5-5", Attempts = 1, CostMicroUsd = cost }];

    private static BudgetReading Reading(BudgetState state) => BudgetCalculator.Compose(new ProcessingBudgetOptions { LimitUsd = 10m }, Nov19, [])
        with { State = state };
}
