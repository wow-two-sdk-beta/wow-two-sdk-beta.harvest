using System.Globalization;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;

/// <summary>Reads a <c>Retry-After</c> header the way the host meant it: a number of seconds, or an HTTP-date to wait
/// until.</summary>
/// <remarks>A host that asks to wait is obeyed, never worked around; pacing waits at least this long.</remarks>
internal static class RetryAfterPolicy
{
    /// <summary>Resolves a <c>Retry-After</c> value to a wait, from either a number of seconds or an HTTP-date measured
    /// against <paramref name="now"/>. A value in the past is no wait; an unreadable value is none.</summary>
    /// <param name="value">The header value, or null.</param>
    /// <param name="now">The clock the HTTP-date form is measured against.</param>
    public static TimeSpan? Resolve(string? value, DateTimeOffset now)
    {
        if (TryParseSeconds(value) is { } seconds)
            return seconds;
        if (TryParseDate(value) is { } when)
            return when <= now ? TimeSpan.Zero : when - now;
        return null;
    }

    /// <summary>Parses the seconds form of <c>Retry-After</c>; null when the value is absent or not a plain count of
    /// seconds.</summary>
    /// <param name="value">The header value, or null.</param>
    public static TimeSpan? TryParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Max(0, seconds))
            : null;
    }

    /// <summary>Parses the HTTP-date form of <c>Retry-After</c>; null when the value is absent or not a date.</summary>
    /// <param name="value">The header value, or null.</param>
    public static DateTimeOffset? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(
            value.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var when)
            ? when
            : null;
    }
}
