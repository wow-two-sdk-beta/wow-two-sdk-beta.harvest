using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;

/// <summary>Normalizes and validates registration requests — the registry's pure rules, with no store access.</summary>
/// <remarks>Each rule returns the error for the first problem it finds, or null; normalizers return the stored form.</remarks>
internal static partial class RegistrationRules
{
    private static readonly IdnMapping Idn = new();

    /// <summary>Lists the families whose handle is a host.</summary>
    private static readonly HashSet<SourceFamily> WebFamilies =
    [
        SourceFamily.Page, SourceFamily.Structured, SourceFamily.Site, SourceFamily.Render, SourceFamily.Api,
        SourceFamily.Feed, SourceFamily.Link, SourceFamily.Listing,
    ];

    /// <summary>Normalizes a tenant slug: trimmed, lowercased, letters, digits and inner hyphens.</summary>
    public static (string? Slug, AppError? Error) NormalizeSlug(string? slug)
    {
        var value = slug?.Trim().ToLowerInvariant() ?? string.Empty;
        return value.Length is > 0 and <= RegistrationConstants.MaxSlugLength && SlugPattern().IsMatch(value)
            ? (value, null)
            : (null, RegistrationErrors.Invalid(
                $"The slug must be 1–{RegistrationConstants.MaxSlugLength} lowercase letters, digits and inner hyphens."));
    }

    /// <summary>Normalizes a display name: trimmed, non-blank, within <paramref name="maxLength"/>.</summary>
    public static (string? Name, AppError? Error) NormalizeName(string? name, int maxLength = RegistrationConstants.MaxNameLength)
    {
        var value = name?.Trim() ?? string.Empty;
        return value.Length is > 0 && value.Length <= maxLength
            ? (value, null)
            : (null, RegistrationErrors.Invalid($"The name must be 1–{maxLength} characters."));
    }

    /// <summary>Normalizes an optional reference: trimmed, null when blank, within the reference limit.</summary>
    public static (string? Value, AppError? Error) NormalizeOptional(string? value, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, null);

        return trimmed.Length <= RegistrationConstants.MaxExternalRefLength
            ? (trimmed, null)
            : (null, RegistrationErrors.Invalid($"The {field} must be at most {RegistrationConstants.MaxExternalRefLength} characters."));
    }

    /// <summary>Validates an API key request against the clock: a name, tenant scopes only, a future expiry.</summary>
    public static AppError? ValidateApiKeyIssue(ApiKeyIssue issue, DateTimeOffset now)
    {
        if (NormalizeName(issue.Name, ApiKeyConstants.MaxNameLength).Error is { } nameError)
            return nameError;
        if (issue.Scopes is not { Count: > 0 })
            return RegistrationErrors.Invalid("An API key must grant at least one scope.");

        foreach (var scope in issue.Scopes)
        {
            if (HarvestScopeConstants.OperatorScopes.Contains(scope))
                return RegistrationErrors.Invalid($"The scope '{scope}' belongs to the operator key and is never issued to a tenant.");
            if (!HarvestScopeConstants.TenantScopes.Contains(scope))
                return RegistrationErrors.Invalid($"The scope '{scope}' is not a Harvest scope.");
        }

        return issue.ExpiresAtUtc is { } expires && expires <= now
            ? RegistrationErrors.Invalid("An API key must expire in the future.")
            : null;
    }

    /// <summary>Validates who owns a container: the pool has no tenant, a private container has one.</summary>
    public static AppError? ValidateContainerOwner(ContainerRegistration registration) => registration.Kind switch
    {
        ContainerKind.Pool when registration.TenantId is not null => RegistrationErrors.Invalid("The pool belongs to no tenant."),
        ContainerKind.Private when registration.TenantId is null => RegistrationErrors.Invalid("A private container needs its tenant."),
        _ => null,
    };

    /// <summary>Normalizes a source handle by family: a host for web families, <c>tg:{username}</c> for Telegram, a
    /// lowercased handle otherwise.</summary>
    public static (string? Handle, AppError? Error) NormalizeHandle(SourceFamily family, string? handle)
    {
        var value = handle?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return (null, RegistrationErrors.Invalid("A source needs a handle."));

        var normalized = family switch
        {
            _ when WebFamilies.Contains(family) => NormalizeHost(value),
            SourceFamily.Telegram => NormalizeTelegram(value),
            _ => NormalizePlain(value),
        };

        return normalized is { Length: > 0 and <= SourceConstants.MaxHandleLength }
            ? (normalized, null)
            : (null, RegistrationErrors.Invalid(family == SourceFamily.Telegram
                ? "A Telegram handle is a public username: 4–32 letters, digits and underscores."
                : WebFamilies.Contains(family)
                    ? "A web source's handle is a host name, such as mstar.uz."
                    : "A source's handle must hold no whitespace."));
    }

    /// <summary>Validates the source fields beyond its handle.</summary>
    public static AppError? ValidateSource(SourceRegistration registration)
    {
        if (registration.Authority is < 0 or > 100)
            return RegistrationErrors.Invalid("A source's authority must be 0–100.");
        if (!SourceConstants.RetentionClasses.Contains(registration.RetentionClass))
            return RegistrationErrors.Invalid($"The retention class '{registration.RetentionClass}' is not seeded.");
        if (registration.CheckPolicy is { } checkPolicy && !SourceConstants.CheckPolicies.Contains(checkPolicy))
            return RegistrationErrors.Invalid($"The check policy '{checkPolicy}' is not seeded.");

        return registration.CapabilityPolicy is { } policy ? ValidateCapabilityPolicy(policy) : null;
    }

    /// <summary>Validates a capability policy: step keys and lanes, non-negative caps.</summary>
    public static AppError? ValidateCapabilityPolicy(CapabilityPolicy policy)
    {
        if (policy.Defaults is { } defaults && ValidateStep("defaults", defaults.Lane, defaults.MaxUsdPerMonth) is { } defaultsError)
            return defaultsError;

        foreach (var (stepKey, step) in policy.Steps)
        {
            if (!IsKey(stepKey))
                return RegistrationErrors.Invalid($"The step key '{stepKey}' must match {StepConstants.KeyPattern}.");
            if (ValidateStep(stepKey, step.Lane, step.MaxUsdPerMonth) is { } stepError)
                return stepError;
        }

        return policy.MonthUsd is < 0 ? RegistrationErrors.Invalid("A monthly cap must not be negative.") : null;
    }

    /// <summary>Validates a pipeline's display name, schedules, step overrides and limits.</summary>
    public static AppError? ValidatePipeline(PipelineDeclaration declaration)
    {
        if (declaration.Name is { } name && NormalizeName(name, PipelineConstants.MaxNameLength).Error is { } nameError)
            return nameError;

        return ValidateSchedules(declaration.Schedules) ?? ValidateStepOverrides(declaration.Steps) ?? ValidateLimits(declaration.Limits);
    }

    /// <summary>Validates schedule rows: schedulable kinds, one row per kind, the spec each mode needs, a known zone.</summary>
    public static AppError? ValidateSchedules(IReadOnlyList<ScheduleDeclaration> schedules)
    {
        var kinds = new HashSet<WorkKind>();
        foreach (var schedule in schedules)
        {
            if (!PipelineConstants.SchedulableKinds.Contains(schedule.Kind))
                return RegistrationErrors.Invalid($"A schedule row cannot run '{schedule.Kind}'; declare a sweep as discover, refresh and check rows.");
            if (!kinds.Add(schedule.Kind))
                return RegistrationErrors.Invalid($"A pipeline holds one schedule row per kind; '{schedule.Kind}' repeats.");
            if (ValidateScheduleSpec(schedule) is { } specError)
                return specError;
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZone, out _))
                return RegistrationErrors.Invalid($"'{schedule.TimeZone}' is not a known time zone.");
            if (schedule.Window is { } window && window.Start == window.End)
                return RegistrationErrors.Invalid("A schedule window must not open and close at the same time.");
        }

        return null;
    }

    /// <summary>Validates step overrides: keys and lanes, non-negative caps.</summary>
    public static AppError? ValidateStepOverrides(IReadOnlyDictionary<string, StepOverride> steps)
    {
        foreach (var (stepKey, step) in steps)
        {
            if (!IsKey(stepKey))
                return RegistrationErrors.Invalid($"The step key '{stepKey}' must match {StepConstants.KeyPattern}.");
            if (ValidateStep(stepKey, step.Lane, step.MaxUsdPerMonth) is { } stepError)
                return stepError;
        }

        return null;
    }

    /// <summary>Validates per-run limits: the filtered caps and positive budgets.</summary>
    public static AppError? ValidateLimits(PipelineLimits limits)
    {
        if (limits.MaxPages is { } pages && pages is < 1 or > PipelineConstants.MaxPagesCap)
            return RegistrationErrors.Invalid($"MaxPages must be 1–{PipelineConstants.MaxPagesCap}.");
        if (limits.MaxItems is { } items && items is < 1 or > PipelineConstants.MaxItemsCap)
            return RegistrationErrors.Invalid($"MaxItems must be 1–{PipelineConstants.MaxItemsCap}.");

        return limits.MaxRequests is <= 0 || limits.MaxBytes is <= 0 || limits.MaxProbes is <= 0
               || (limits.MaxDuration is { } duration && duration <= TimeSpan.Zero)
            ? RegistrationErrors.Invalid("Run budgets must be positive.")
            : null;
    }

    /// <summary>Builds the canonical scope key: <c>*</c> for the whole source, else the normalized URL and the parameters
    /// sorted by name, query-encoded.</summary>
    public static (string? ScopeKey, AppError? Error) CanonicalScopeKey(PipelineScope? scope)
    {
        var url = scope?.Url?.Trim();
        var parameters = scope?.Parameters ?? new Dictionary<string, string>();
        if (string.IsNullOrEmpty(url) && parameters.Count == 0)
            return (PipelineConstants.WholeScopeKey, null);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(url))
        {
            if (NormalizeScopeUrl(url) is not { } normalizedUrl)
                return (null, RegistrationErrors.Invalid("A pipeline's scope URL must be an absolute http(s) URL."));
            parts.Add("url=" + Uri.EscapeDataString(normalizedUrl));
        }

        foreach (var (name, value) in parameters.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name))
                return (null, RegistrationErrors.Invalid("A pipeline's scope parameter needs a name."));
            parts.Add(Uri.EscapeDataString(name.Trim()) + "=" + Uri.EscapeDataString(value?.Trim() ?? string.Empty));
        }

        var key = string.Join('&', parts);
        return key.Length <= PipelineConstants.MaxScopeKeyLength
            ? (key, null)
            : (null, RegistrationErrors.Invalid($"A pipeline's scope must stay within {PipelineConstants.MaxScopeKeyLength} characters."));
    }

    /// <summary>Normalizes a scope URL: lowercase scheme and host, no default port or fragment, query sorted by name.</summary>
    private static string? NormalizeScopeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;

        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(pair => pair, StringComparer.Ordinal);
        var builder = new StringBuilder()
            .Append(uri.Scheme).Append("://").Append(uri.IdnHost.ToLowerInvariant());
        if (!uri.IsDefaultPort)
            builder.Append(':').Append(uri.Port.ToString(CultureInfo.InvariantCulture));
        builder.Append(uri.AbsolutePath);
        var sortedQuery = string.Join('&', query);
        if (sortedQuery.Length > 0)
            builder.Append('?').Append(sortedQuery);

        return builder.ToString();
    }

    private static AppError? ValidateScheduleSpec(ScheduleDeclaration schedule) => schedule.Mode switch
    {
        ScheduleMode.Interval when schedule.Every is not { } every || every < PipelineConstants.MinInterval =>
            RegistrationErrors.Invalid($"An interval schedule runs every {PipelineConstants.MinInterval.TotalMinutes:0} minutes or more."),
        ScheduleMode.Interval when schedule.Cron is not null => RegistrationErrors.Invalid("An interval schedule takes no cron expression."),
        ScheduleMode.Cron when !IsCron(schedule.Cron) =>
            RegistrationErrors.Invalid("A cron schedule needs a five- or six-field cron expression."),
        ScheduleMode.Cron when schedule.Every is not null => RegistrationErrors.Invalid("A cron schedule takes no interval."),
        ScheduleMode.Adaptive when schedule.Every is not { } min || min < PipelineConstants.MinInterval
                                   || schedule.MaxEvery is not { } max || max < min =>
            RegistrationErrors.Invalid("An adaptive schedule needs a shortest interval of at least five minutes and a longest at or above it."),
        _ => null,
    };

    /// <summary>Validates one step's lane and monthly cap.</summary>
    private static AppError? ValidateStep(string stepKey, string? lane, decimal? maxUsdPerMonth)
    {
        if (lane is not null && !IsKey(lane))
            return RegistrationErrors.Invalid($"The lane of step '{stepKey}' must match {StepConstants.KeyPattern}.");

        return maxUsdPerMonth is < 0 ? RegistrationErrors.Invalid($"The step '{stepKey}' has a negative monthly cap.") : null;
    }

    private static bool IsKey(string value) => KeyPattern().IsMatch(value);

    private static bool IsCron(string? cron) =>
        cron is not null && cron.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is 5 or 6;

    private static string? NormalizeHost(string value)
    {
        var host = value.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.Host
            : value.Split('/', 2)[0].Split(':', 2)[0];
        host = host.Trim().TrimEnd('.');
        if (host.Length == 0)
            return null;

        try
        {
            host = Idn.GetAscii(host).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        return Uri.CheckHostName(host) == UriHostNameType.Dns ? host : null;
    }

    private static string? NormalizeTelegram(string value)
    {
        var name = value.ToLowerInvariant();
        foreach (var prefix in new[] { "https://t.me/s/", "https://t.me/", "http://t.me/", "t.me/s/", "t.me/", SourceConstants.TelegramHandlePrefix, "@" })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        name = name.Split('/', 2)[0];
        return TelegramPattern().IsMatch(name) ? SourceConstants.TelegramHandlePrefix + name : null;
    }

    private static string? NormalizePlain(string value)
    {
        var handle = value.TrimStart('@').ToLowerInvariant();
        return handle.Length > 0 && !handle.Any(char.IsWhiteSpace) ? handle : null;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(StepConstants.KeyPattern)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{3,31}$")]
    private static partial Regex TelegramPattern();
}
