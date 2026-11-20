using Cronos;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Computes future schedule slots in their declared time zone and daily window.</summary>
internal static class ScheduleCalculator
{
    internal static DateTimeOffset? Next(ScheduleModel schedule, DateTimeOffset after, bool first = false)
    {
        if (!schedule.Enabled || schedule.Mode == ScheduleMode.Manual)
            return null;

        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        if (schedule.Stage?.StartsOn is { } startsOn
            && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime) < startsOn)
        {
            after = LocalInstant(startsOn.ToDateTime(TimeOnly.MinValue), zone);
            first = true;
        }
        DateTimeOffset? due;
        if (schedule.Mode == ScheduleMode.Cron)
        {
            string expression = schedule.Cron ?? throw new ArgumentException("A cron expression is required.");
            CronFormat format = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 6
                ? CronFormat.IncludeSeconds : CronFormat.Standard;
            due = CronExpression.Parse(expression, format).GetNextOccurrence(after, zone, inclusive: first);
        }
        else
        {
            due = first ? after : after + (schedule.Every ?? TimeSpan.FromHours(1));
        }

        if (due is null || schedule.Window is null)
            return due?.ToUniversalTime();

        for (int attempt = 0; attempt < 370 && due is not null; attempt++)
        {
            DateTime local = TimeZoneInfo.ConvertTime(due.Value, zone).DateTime;
            TimeOnly time = TimeOnly.FromDateTime(local);
            TimeOnly start = schedule.Window.Start;
            TimeOnly end = schedule.Window.End;
            bool allowed = start == end || (start < end ? time >= start && time < end : time >= start || time < end);
            if (allowed)
                return due.Value.ToUniversalTime();

            DateTime next = local.Date.Add(start.ToTimeSpan());
            if (next <= local)
                next = next.AddDays(1);
            DateTimeOffset windowStart = LocalInstant(next, zone);
            due = schedule.Mode == ScheduleMode.Cron ? Next(schedule with { Window = null }, windowStart, first: true) : windowStart;
        }

        return null;
    }

    private static DateTimeOffset LocalInstant(DateTime local, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return new(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
