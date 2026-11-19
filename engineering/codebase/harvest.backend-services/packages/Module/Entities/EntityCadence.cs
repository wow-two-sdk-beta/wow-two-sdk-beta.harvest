namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Represents an inferred cadence: its name, how sure it is, 0 to 1, and its period in days when it has one.</summary>
internal sealed record CadenceEstimate(string Cadence, decimal Confidence, int? PeriodDays);

/// <summary>Infers how often an event runs from its stated starts and predicts its next run.</summary>
/// <remarks>
///   - two or more distinct stated starts are needed; fewer is <c>unknown</c>
///   - the median gap picks the period: ~365 days annual, ~182 semiannual, ~91 quarterly, ~30 monthly, each within its
///     configured tolerance; anything else is <c>irregular</c>
///   - confidence is the mean fit of every gap to that period (1 on the period, 0 at the tolerance's edge), scaled by
///     the sample: one gap counts ⅔, two ⅘, more approach 1
///   - the prediction is the last start plus the period in calendar units (a year, six months, three months, a month),
///     keeping the last run's length
/// </remarks>
internal static class EntityCadence
{
    private static readonly (string Cadence, int Days)[] Periods = [("annual", 365), ("semiannual", 182), ("quarterly", 91), ("monthly", 30)];

    /// <summary>Infers the cadence of <paramref name="starts"/>.</summary>
    /// <param name="starts">The stated starts of the event's real occurrences, in any order.</param>
    /// <param name="options">The tolerances.</param>
    internal static CadenceEstimate Infer(IEnumerable<DateOnly> starts, EntityCadenceOptions options)
    {
        var dates = starts.Distinct().Order().ToArray();
        if (dates.Length < 2)
            return new("unknown", 0m, null);
        var gaps = dates.Zip(dates.Skip(1), (earlier, later) => later.DayNumber - earlier.DayNumber).ToArray();
        double median = Median(gaps);
        double sample = gaps.Length / (gaps.Length + 0.5);
        foreach (var (cadence, days) in Periods)
        {
            int tolerance = Tolerance(cadence, options);
            if (Math.Abs(median - days) > tolerance)
                continue;
            double fit = gaps.Average(gap => Math.Max(0, 1 - Math.Abs(gap - days) / (double)(tolerance + 1)));
            return new(cadence, Round(fit * sample), days);
        }
        return new("irregular", Round(sample), null);
    }

    /// <summary>Returns the next start after <paramref name="last"/> for a regular cadence, or null for another one.</summary>
    internal static DateOnly? Next(DateOnly last, string cadence) => cadence switch
    {
        "annual" => last.AddYears(1),
        "semiannual" => last.AddMonths(6),
        "quarterly" => last.AddMonths(3),
        "monthly" => last.AddMonths(1),
        _ => null,
    };

    /// <summary>Whether a cadence predicts: regular, and at least as sure as the configured confidence.</summary>
    internal static bool Predicts(CadenceEstimate estimate, EntityCadenceOptions options) =>
        estimate.PeriodDays is not null && (double)estimate.Confidence >= options.PredictMinConfidence;

    private static int Tolerance(string cadence, EntityCadenceOptions options) => cadence switch
    {
        "annual" => options.AnnualDays,
        "semiannual" => options.SemiannualDays,
        "quarterly" => options.QuarterlyDays,
        _ => options.MonthlyDays,
    };

    private static double Median(int[] values)
    {
        var sorted = values.Order().ToArray();
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    private static decimal Round(double value) => Math.Round((decimal)Math.Clamp(value, 0, 1), 3, MidpointRounding.AwayFromZero);
}
