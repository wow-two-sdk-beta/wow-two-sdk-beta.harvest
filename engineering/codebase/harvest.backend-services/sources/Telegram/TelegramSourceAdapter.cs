using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

/// <summary>Declares the bounded read of one public Telegram channel's web preview, one source per channel.</summary>
/// <remarks>Every channel source shares the host key <see cref="TelegramChannels.Host"/>, so the engine paces all of them
/// through one lane, <see cref="RequestGap"/> apart. A run reads the newest page and at most
/// <see cref="MaxPagesPerRun"/> pages in all.</remarks>
public sealed class TelegramSourceAdapter : ISourceAdapter, ISourceConfigurationProvider, ISourcePipelineValidator, ISourceRunLimitsResolver
{
    /// <summary>The stable private reader key.</summary>
    public const string AdapterKey = "telegram-preview";

    /// <summary>The most preview pages one run reads: the newest, then up to two earlier ones.</summary>
    public const int MaxPagesPerRun = 3;

    /// <summary>The least gap between two requests to the preview host.</summary>
    public static readonly TimeSpan RequestGap = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public string Key => AdapterKey;

    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Telegram;

    /// <inheritdoc />
    public bool Serves(SourceModel source) => source.Family == Family && TelegramChannels.TryReadHandle(source.Handle, out _);

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source) => new()
    {
        SourceUrl = TelegramChannels.TryReadHandle(source.Handle, out string username) ? TelegramChannels.PreviewUrl(username) : null,
        HostKey = TelegramChannels.Host,
        Steps = [new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = new() { MinDelay = RequestGap } }],
        Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
        {
            [WorkKind.Capture] = ["enumerate"],
            [WorkKind.Sweep] = ["enumerate"],
        },
    };

    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source) => new()
    {
        DisplayName = "Telegram channel preview",
        Message = "Reads the channel's public web preview with no account, bot or proxy. Each post links to its t.me permalink; access is checked on each run.",
        ScopeSchema = JsonSerializer.SerializeToElement(new
        {
            schema = "harvest.scope.v1", type = "object",
            properties = new
            {
                url = new
                {
                    type = "string", format = "uri", title = "Channel preview URL",
                    @const = TelegramChannels.TryReadHandle(source.Handle, out string username) ? TelegramChannels.PreviewUrl(username) : null,
                    description = "Save the public preview page of the channel, https://t.me/s/{username}.",
                },
            },
            required = new[] { "url" }, additionalProperties = false,
            limits = new
            {
                maxPages = new
                {
                    minimum = 1, maximum = MaxPagesPerRun, @default = MaxPagesPerRun, title = "Pages per run",
                    description = "Reads the newest page, then earlier pages, up to this many.",
                },
            },
        }),
        PostView = new() { ShowSourceText = true },
    };

    /// <inheritdoc />
    public Result ValidatePipeline(SourceModel source, PipelineScope scope, PipelineLimits limits)
    {
        if (!TelegramChannels.TryReadHandle(source.Handle, out string username)
            || scope.Parameters.Count > 0 || scope.RestrictToPath
            || scope.Url is not null && !(TelegramChannels.TryReadSeed(scope.Url, out string scoped) && scoped == username))
            return Result.Fail(RegistrationErrors.Invalid(
                "Telegram pipelines read the source's own channel preview, https://t.me/s/{username}, without query overrides or path restriction."));
        if (limits.MaxPages is < 1 or > MaxPagesPerRun)
            return Result.Fail(RegistrationErrors.Invalid($"A Telegram preview run reads 1–{MaxPagesPerRun} pages."));
        if (limits.MaxItems is < 1 or > PipelineConstants.MaxItemsCap)
            return Result.Fail(RegistrationErrors.Invalid($"Telegram posts per run must be 1–{PipelineConstants.MaxItemsCap}."));
        return Result.Ok();
    }

    /// <inheritdoc />
    public PipelineLimits ResolveRunLimits(SourceModel source, PipelineLimits limits, WorkKind kind) =>
        kind is WorkKind.Capture or WorkKind.Sweep
            ? limits with { MaxPages = Math.Min(limits.MaxPages ?? MaxPagesPerRun, MaxPagesPerRun) }
            : limits;
}
