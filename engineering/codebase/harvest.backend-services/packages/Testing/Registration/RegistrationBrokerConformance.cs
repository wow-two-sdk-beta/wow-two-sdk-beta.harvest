using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

/// <summary>The conformance suite of <see cref="IRegistrationBroker"/>: one set of rules every implementation passes.</summary>
/// <remarks>Derive a test class per implementation — the module, the client against a host, the fake — and supply the
/// broker and the clock it stamps rows with. Every case makes its own tenants and sources, so the suite runs over a store
/// that other cases share; the one pool is reused when it exists.</remarks>
public abstract class RegistrationBrokerConformance
{
    /// <summary>Gets the implementation under test.</summary>
    protected abstract IRegistrationBroker Broker { get; }

    /// <summary>Gets the clock the implementation stamps rows with.</summary>
    protected abstract TimeProvider Clock { get; }

    // ── Tenants ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A tenant registers under its trimmed, lowercased slug and reads back.</summary>
    [Fact]
    public async Task RegisterTenant_ShouldNormalizeTheSlugAndReadBack_WhenTheSlugIsFree()
    {
        var unique = Unique();

        var tenant = Value(await Broker.RegisterTenantAsync(new TenantRegistration { Slug = $"  Events-{unique.ToUpperInvariant()} ", Name = " Events " }));

        tenant.Slug.Should().Be($"events-{unique}");
        tenant.Name.Should().Be("Events");
        tenant.State.Should().Be(TenantState.Active);
        tenant.PlatformCredentialsAllowed.Should().BeFalse();
        tenant.CreatedAtUtc.Should().BeCloseTo(Clock.GetUtcNow(), TimeSpan.FromMinutes(1));
        Value(await Broker.GetTenantAsync(tenant.Id)).Should().BeEquivalentTo(tenant);
        Value(await Broker.ListTenantsAsync()).Should().ContainEquivalentOf(tenant);
    }

    /// <summary>A taken slug conflicts.</summary>
    [Fact]
    public async Task RegisterTenant_ShouldConflict_WhenTheSlugIsTaken()
    {
        var tenant = await TenantAsync();

        Error(await Broker.RegisterTenantAsync(new TenantRegistration { Slug = tenant.Slug.ToUpperInvariant(), Name = "Again" }))
            .Type.Should().Be(AppErrorType.Conflict);
    }

    /// <summary>A malformed slug or a blank name fails validation.</summary>
    /// <param name="slug">The slug.</param>
    /// <param name="name">The name.</param>
    [Theory]
    [InlineData("", "Name")]
    [InlineData("-leading", "Name")]
    [InlineData("has space", "Name")]
    [InlineData("under_score", "Name")]
    [InlineData("fine", "   ")]
    public async Task RegisterTenant_ShouldFailValidation_WhenTheSlugOrNameIsMalformed(string slug, string name)
    {
        Error(await Broker.RegisterTenantAsync(new TenantRegistration { Slug = slug, Name = name }))
            .Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>An unknown tenant is not found.</summary>
    [Fact]
    public async Task GetTenant_ShouldReturnNotFound_WhenNoTenantHasTheId()
    {
        Error(await Broker.GetTenantAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    // ── API keys ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An issued key returns its marked secret once; reads show the prefix and metadata only.</summary>
    [Fact]
    public async Task IssueApiKey_ShouldReturnTheSecretOnceAndKeepTheMetadata_WhenTheScopesAreTenantScopes()
    {
        var tenant = await TenantAsync();
        var expires = Clock.GetUtcNow().AddDays(30);

        var issued = Value(await Broker.IssueApiKeyAsync(tenant.Id, new ApiKeyIssue
        {
            Name = " events-service ",
            Scopes = [HarvestScopeConstants.PipelinesManage, HarvestScopeConstants.EventsRead],
            ExpiresAtUtc = expires,
        }));

        issued.Secret.Should().StartWith(ApiKeyConstants.Marker).And.HaveLength(ApiKeyConstants.Marker.Length + ApiKeyConstants.RandomLength);
        issued.Secret.Should().StartWith(issued.Key.Prefix);
        issued.Key.Should().Match<ApiKeyModel>(key => key.TenantId == tenant.Id && key.Name == "events-service"
            && key.State == ApiKeyState.Active && key.RevokedAtUtc == null);
        issued.Key.Scopes.Should().BeEquivalentTo([HarvestScopeConstants.PipelinesManage, HarvestScopeConstants.EventsRead]);
        issued.Key.ExpiresAtUtc.Should().BeCloseTo(expires, TimeSpan.FromMilliseconds(1));
        Value(await Broker.GetApiKeyAsync(issued.Key.Id)).Should().BeEquivalentTo(issued.Key);
        Value(await Broker.ListApiKeysAsync(tenant.Id)).Should().ContainSingle().Which.Should().BeEquivalentTo(issued.Key);
    }

    /// <summary>A key grants tenant scopes only: no operator scope, no unknown scope, at least one.</summary>
    /// <param name="scopes">The comma-separated scopes asked for.</param>
    [Theory]
    [InlineData(HarvestScopeConstants.TenantsManage)]
    [InlineData(HarvestScopeConstants.SourcesManage)]
    [InlineData("pipelines:delete")]
    [InlineData("")]
    public async Task IssueApiKey_ShouldFailValidation_WhenAScopeIsNotATenantScope(string scopes)
    {
        var tenant = await TenantAsync();

        var result = await Broker.IssueApiKeyAsync(tenant.Id, new ApiKeyIssue
        {
            Name = "key",
            Scopes = scopes.Split(',', StringSplitOptions.RemoveEmptyEntries),
        });

        Error(result).Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>A key that would expire in the past fails validation.</summary>
    [Fact]
    public async Task IssueApiKey_ShouldFailValidation_WhenTheExpiryHasPassed()
    {
        var tenant = await TenantAsync();

        var result = await Broker.IssueApiKeyAsync(tenant.Id, new ApiKeyIssue
        {
            Name = "key",
            Scopes = [HarvestScopeConstants.PostsRead],
            ExpiresAtUtc = Clock.GetUtcNow().AddMinutes(-1),
        });

        Error(result).Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>A key for an unknown tenant is not found.</summary>
    [Fact]
    public async Task IssueApiKey_ShouldReturnNotFound_WhenTheTenantIsUnknown()
    {
        Error(await Broker.IssueApiKeyAsync(Guid.NewGuid(), new ApiKeyIssue { Name = "key", Scopes = [HarvestScopeConstants.PostsRead] }))
            .Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>A revoked key stays revoked, and revoking it again changes nothing.</summary>
    [Fact]
    public async Task RevokeApiKey_ShouldRevokeOnce_WhenTheTenantHoldsTheKey()
    {
        var tenant = await TenantAsync();
        var issued = Value(await Broker.IssueApiKeyAsync(tenant.Id, new ApiKeyIssue { Name = "key", Scopes = [HarvestScopeConstants.PostsRead] }));

        var revoked = Value(await Broker.RevokeApiKeyAsync(tenant.Id, issued.Key.Id));
        var again = Value(await Broker.RevokeApiKeyAsync(tenant.Id, issued.Key.Id));

        revoked.State.Should().Be(ApiKeyState.Revoked);
        revoked.RevokedAtUtc.Should().NotBeNull();
        again.RevokedAtUtc.Should().Be(revoked.RevokedAtUtc);
        Value(await Broker.GetApiKeyAsync(issued.Key.Id)).State.Should().Be(ApiKeyState.Revoked);
    }

    /// <summary>A key revoked through another tenant, or no key at all, is not found.</summary>
    [Fact]
    public async Task RevokeApiKey_ShouldReturnNotFound_WhenTheTenantDoesNotHoldTheKey()
    {
        var owner = await TenantAsync();
        var other = await TenantAsync();
        var issued = Value(await Broker.IssueApiKeyAsync(owner.Id, new ApiKeyIssue { Name = "key", Scopes = [HarvestScopeConstants.PostsRead] }));

        Error(await Broker.RevokeApiKeyAsync(other.Id, issued.Key.Id)).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.RevokeApiKeyAsync(owner.Id, Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.GetApiKeyAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    // ── Containers ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A private container belongs to its tenant and lists under it only.</summary>
    [Fact]
    public async Task RegisterContainer_ShouldKeepThePrivateContainerUnderItsTenant_WhenTheTenantExists()
    {
        var tenant = await TenantAsync();
        var other = await TenantAsync();

        var container = Value(await Broker.RegisterContainerAsync(new ContainerRegistration
        {
            Kind = ContainerKind.Private,
            TenantId = tenant.Id,
            ExternalRef = " ocharo-fashion ",
        }));
        await PrivateContainerAsync(other);

        container.Should().Match<ContainerModel>(model => model.Kind == ContainerKind.Private && model.TenantId == tenant.Id
            && model.ExternalRef == "ocharo-fashion" && model.State == ContainerState.Active && !model.EmbeddingsEnabled);
        Value(await Broker.GetContainerAsync(container.Id)).Should().BeEquivalentTo(container);
        Value(await Broker.ListContainersAsync(tenant.Id)).Should().ContainSingle().Which.Id.Should().Be(container.Id);
        Value(await Broker.ListContainersAsync()).Select(model => model.Id).Should().Contain(container.Id);
    }

    /// <summary>The pool belongs to no tenant; a private container has one.</summary>
    [Fact]
    public async Task RegisterContainer_ShouldFailValidation_WhenTheOwnerDoesNotFitTheKind()
    {
        var tenant = await TenantAsync();

        Error(await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private })).Type.Should().Be(AppErrorType.Validation);
        Error(await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool, TenantId = tenant.Id }))
            .Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>A container for an unknown tenant is not found.</summary>
    [Fact]
    public async Task RegisterContainer_ShouldReturnNotFound_WhenTheTenantIsUnknown()
    {
        Error(await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = Guid.NewGuid() }))
            .Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.GetContainerAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>The instance holds one pool.</summary>
    [Fact]
    public async Task RegisterContainer_ShouldConflict_WhenThePoolExists()
    {
        await PoolAsync();

        Error(await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).Type.Should().Be(AppErrorType.Conflict);
    }

    /// <summary>A tenant names each container once.</summary>
    [Fact]
    public async Task RegisterContainer_ShouldConflict_WhenTheTenantUsesTheExternalReference()
    {
        var tenant = await TenantAsync();
        await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id, ExternalRef = "catalog" });

        Error(await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id, ExternalRef = "catalog" }))
            .Type.Should().Be(AppErrorType.Conflict);
    }

    // ── Sources ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A web source keeps its host as handle and ships with the permissive posture switches.</summary>
    [Fact]
    public async Task RegisterSource_ShouldReduceAUrlToItsHostAndApplyPermissiveDefaults_WhenOnlyTheRequiredFieldsAreGiven()
    {
        var container = await PrivateContainerAsync(await TenantAsync());

        var source = Value(await Broker.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id,
            Family = SourceFamily.Structured,
            Handle = " https://WWW.Mstar.UZ/events?page=2 ",
            Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete,
            Posture = SourcePosture.OwnFetch,
        }));

        source.Should().Match<SourceModel>(model => model.Handle == "www.mstar.uz" && model.ContainerId == container.Id
            && model.Permission == SourcePermission.Open && model.AiAllowed && model.PoolShareable
            && model.RobotsPolicy == RobotsPolicy.Obey && model.ConsentBasis == ConsentBasis.None && model.Authority == SourceConstants.DefaultAuthority
            && model.RetentionClass == SourceConstants.StandardRetentionClass && model.CheckPolicy == null && model.State == SourceState.Active);
        Value(await Broker.GetSourceAsync(source.Id)).Should().BeEquivalentTo(source);
        Value(await Broker.ListSourcesAsync(container.Id)).Should().ContainSingle().Which.Id.Should().Be(source.Id);
    }

    /// <summary>A Telegram handle normalizes to <c>tg:{username}</c>, so two spellings of one channel conflict.</summary>
    [Fact]
    public async Task RegisterSource_ShouldConflict_WhenAnotherSpellingOfTheHandleExists()
    {
        var container = await PrivateContainerAsync(await TenantAsync());

        var first = Value(await Broker.RegisterSourceAsync(Source(container.Id, SourceFamily.Telegram, "@AfishaUz")));
        var second = await Broker.RegisterSourceAsync(Source(container.Id, SourceFamily.Telegram, "https://t.me/s/afishauz"));

        first.Handle.Should().Be("tg:afishauz");
        Error(second).Type.Should().Be(AppErrorType.Conflict);
    }

    /// <summary>A source in an unknown container is not found.</summary>
    [Fact]
    public async Task RegisterSource_ShouldReturnNotFound_WhenTheContainerIsUnknown()
    {
        Error(await Broker.RegisterSourceAsync(Source(Guid.NewGuid(), SourceFamily.Page, "example.com"))).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.GetSourceAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.ListSourcesAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>A malformed handle, authority, retention class, check policy or capability policy fails validation.</summary>
    [Fact]
    public async Task RegisterSource_ShouldFailValidation_WhenAFieldIsMalformed()
    {
        var container = await PrivateContainerAsync(await TenantAsync());
        var valid = Source(container.Id, SourceFamily.Page, "example.com");

        SourceRegistration[] malformed =
        [
            valid with { Handle = "not a host!" },
            valid with { Family = SourceFamily.Telegram, Handle = "@a" },
            valid with { Authority = 101 },
            valid with { RetentionClass = "forever" },
            valid with { CheckPolicy = "hourly" },
            valid with { CapabilityPolicy = new CapabilityPolicy { Defaults = new StepCapabilityPolicy { Lane = "Lane One" } } },
            valid with { CapabilityPolicy = new CapabilityPolicy { Steps = new Dictionary<string, StepCapabilityPolicy> { ["detail"] = new() { MaxUsdPerMonth = -1 } } } },
            valid with { CapabilityPolicy = new CapabilityPolicy { Steps = new Dictionary<string, StepCapabilityPolicy> { ["has_underscore"] = new() } } },
        ];

        foreach (var registration in malformed)
            Error(await Broker.RegisterSourceAsync(registration)).Type.Should().Be(AppErrorType.Validation, registration.ToString());
    }

    /// <summary>A source's posture switches, check policy and capability policy read back as registered.</summary>
    [Fact]
    public async Task RegisterSource_ShouldKeepThePostureAndCapabilityPolicy_WhenTheyAreGiven()
    {
        var container = await PrivateContainerAsync(await TenantAsync());
        var policy = new CapabilityPolicy
        {
            Defaults = new StepCapabilityPolicy { MaxUsdPerMonth = 10m },
            Steps = new Dictionary<string, StepCapabilityPolicy>
            {
                ["contact"] = new() { Lane = "contact", MaxUsdPerMonth = 3m },
            },
            MonthUsd = 30m,
        };

        var source = Value(await Broker.RegisterSourceAsync(Source(container.Id, SourceFamily.Listing, "olx.uz") with
        {
            Coverage = SourceCoverage.Filtered,
            Completeness = SourceCompleteness.Window,
            Permission = SourcePermission.Restricted,
            AiAllowed = false,
            PoolShareable = false,
            RobotsPolicy = RobotsPolicy.Ignore,
            Authority = 80,
            RetentionClass = "telegram",
            CheckPolicy = "web-direct",
            CapabilityPolicy = policy,
        }));

        source.Should().Match<SourceModel>(model => model.Permission == SourcePermission.Restricted && !model.AiAllowed && !model.PoolShareable
            && model.RobotsPolicy == RobotsPolicy.Ignore && model.Authority == 80 && model.RetentionClass == "telegram" && model.CheckPolicy == "web-direct");
        source.CapabilityPolicy.Should().BeEquivalentTo(policy);
        Value(await Broker.GetSourceAsync(source.Id)).CapabilityPolicy.Should().BeEquivalentTo(policy);
    }

    // ── Pipelines ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A whole-site pipeline keys on <c>*</c>, starts active over an open source and reads back with its rows.</summary>
    [Fact]
    public async Task DeclarePipeline_ShouldStoreTheWholeSitePipelineWithItsSchedulesAndSteps_WhenTheSourceIsOpen()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole);
        var declaration = new PipelineDeclaration
        {
            ContainerId = container.Id,
            SourceId = source.Id,
            Name = " mstar sweep ",
            Schedules =
            [
                new ScheduleDeclaration { Kind = WorkKind.Discover, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) },
                new ScheduleDeclaration
                {
                    Kind = WorkKind.Refresh, Mode = ScheduleMode.Cron, Cron = "0 2 * * 1",
                    Window = new ScheduleWindow { Start = new TimeOnly(1, 0), End = new TimeOnly(6, 0) },
                },
                new ScheduleDeclaration { Kind = WorkKind.Check, Mode = ScheduleMode.Manual, Enabled = false },
            ],
            Steps = new Dictionary<string, StepOverride>
            {
                ["contact"] = new() { Enabled = true, Lane = "contact", MaxUsdPerMonth = 5m },
            },
            Limits = new PipelineLimits { MaxRequests = 5000, MaxDuration = TimeSpan.FromHours(2) },
            Ai = AiMode.Gaps,
            Pinned = true,
        };

        var pipeline = Value(await Broker.DeclarePipelineAsync(declaration));

        pipeline.Should().Match<PipelineModel>(model => model.ScopeKey == PipelineConstants.WholeScopeKey && model.State == PipelineState.Active
            && model.PauseReason == null && model.Name == "mstar sweep" && model.Pinned && model.Ai == AiMode.Gaps);
        pipeline.Schedules.Select(schedule => schedule.Kind).Should().Equal(WorkKind.Discover, WorkKind.Refresh, WorkKind.Check);
        pipeline.Schedules[0].Every.Should().Be(TimeSpan.FromHours(1));
        pipeline.Schedules[1].Should().Match<ScheduleModel>(schedule => schedule.Cron == "0 2 * * 1"
            && schedule.TimeZone == PipelineConstants.DefaultTimeZone && schedule.Window!.End == new TimeOnly(6, 0));
        pipeline.Schedules[2].Enabled.Should().BeFalse();
        pipeline.Schedules.Should().OnlyContain(schedule => schedule.NextDueAtUtc == null);
        pipeline.Steps.Should().BeEquivalentTo(declaration.Steps);
        pipeline.Limits.Should().BeEquivalentTo(declaration.Limits);
        Value(await Broker.GetPipelineAsync(pipeline.Id)).Should().BeEquivalentTo(pipeline);
        Value(await Broker.ListPipelinesAsync(container.Id)).Should().ContainSingle().Which.Id.Should().Be(pipeline.Id);
    }

    /// <summary>The scope key is canonical, so the same filter in another order is the same pipeline.</summary>
    [Fact]
    public async Task DeclarePipeline_ShouldConflict_WhenTheSameScopeIsDeclaredInAnotherOrder()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Filtered);
        var scope = new PipelineScope
        {
            Url = "https://OLX.uz/nedvizhimost/kvartiry/arenda?search[district]=1&currency=UZS#top",
            Parameters = new Dictionary<string, string> { ["region"] = "tashkent", ["category"] = "rent" },
        };

        var first = Value(await Broker.DeclarePipelineAsync(Pipeline(container, source) with { Scope = scope }));
        var second = await Broker.DeclarePipelineAsync(Pipeline(container, source) with
        {
            Scope = new PipelineScope
            {
                Url = "https://olx.uz/nedvizhimost/kvartiry/arenda?currency=UZS&search[district]=1",
                Parameters = new Dictionary<string, string> { ["category"] = " rent ", ["region"] = "tashkent" },
            },
        });

        first.ScopeKey.Should().NotBe(PipelineConstants.WholeScopeKey).And.StartWith("url=https%3A%2F%2Folx.uz%2F");
        first.ScopeKey.Should().EndWith("&category=rent&region=tashkent");
        Error(second).Type.Should().Be(AppErrorType.Conflict);
        Value(await Broker.DeclarePipelineAsync(Pipeline(container, source) with
        {
            Scope = new PipelineScope { Parameters = new Dictionary<string, string> { ["region"] = "samarkand" } },
        })).ScopeKey.Should().Be("region=samarkand");
    }

    /// <summary>A pipeline over a restricted source waits for approval.</summary>
    [Fact]
    public async Task DeclarePipeline_ShouldWaitForApproval_WhenTheSourceIsRestricted()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole, SourcePermission.Restricted);

        Value(await Broker.DeclarePipelineAsync(Pipeline(container, source))).State.Should().Be(PipelineState.PendingApproval);
    }

    /// <summary>A denied source, a filtered source without a scope, or a source of another container fails validation.</summary>
    [Fact]
    public async Task DeclarePipeline_ShouldFailValidation_WhenTheSourceCannotCarryThePipeline()
    {
        var (deniedContainer, denied) = await SourceAsync(SourceCoverage.Whole, SourcePermission.Denied);
        var (filteredContainer, filtered) = await SourceAsync(SourceCoverage.Filtered);
        var elsewhere = await PrivateContainerAsync(await TenantAsync());

        Error(await Broker.DeclarePipelineAsync(Pipeline(deniedContainer, denied))).Type.Should().Be(AppErrorType.Validation);
        Error(await Broker.DeclarePipelineAsync(Pipeline(filteredContainer, filtered))).Type.Should().Be(AppErrorType.Validation);
        Error(await Broker.DeclarePipelineAsync(Pipeline(elsewhere, filtered) with
        {
            Scope = new PipelineScope { Parameters = new Dictionary<string, string> { ["q"] = "x" } },
        })).Type.Should().Be(AppErrorType.Validation);
    }

    /// <summary>An unknown container or source is not found.</summary>
    [Fact]
    public async Task DeclarePipeline_ShouldReturnNotFound_WhenTheContainerOrSourceIsUnknown()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole);

        Error(await Broker.DeclarePipelineAsync(Pipeline(container, source) with { ContainerId = Guid.NewGuid() })).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.DeclarePipelineAsync(Pipeline(container, source) with { SourceId = Guid.NewGuid() })).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.GetPipelineAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
        Error(await Broker.ListPipelinesAsync(Guid.NewGuid())).Type.Should().Be(AppErrorType.NotFound);
    }

    /// <summary>Malformed schedules, step overrides or limits fail validation.</summary>
    [Fact]
    public async Task DeclarePipeline_ShouldFailValidation_WhenSchedulesStepsOrLimitsAreMalformed()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole);
        var valid = Pipeline(container, source);
        ScheduleDeclaration Every(TimeSpan every) => new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = every };

        PipelineDeclaration[] malformed =
        [
            valid with { Schedules = [Every(TimeSpan.FromMinutes(1))] },
            valid with { Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Cron, Cron = "hourly" }] },
            valid with { Schedules = [Every(TimeSpan.FromHours(1)), Every(TimeSpan.FromHours(2))] },
            valid with { Schedules = [new ScheduleDeclaration { Kind = WorkKind.Sweep, Mode = ScheduleMode.Manual }] },
            valid with { Schedules = [Every(TimeSpan.FromHours(1)) with { TimeZone = "Mars/Olympus" }] },
            valid with { Schedules = [new ScheduleDeclaration { Kind = WorkKind.Check, Mode = ScheduleMode.Adaptive, Every = TimeSpan.FromHours(2), MaxEvery = TimeSpan.FromHours(1) }] },
            valid with { Steps = new Dictionary<string, StepOverride> { ["detail"] = new() { Lane = "Lane One" } } },
            valid with { Steps = new Dictionary<string, StepOverride> { ["detail"] = new() { MaxUsdPerMonth = -1 } } },
            valid with { Steps = new Dictionary<string, StepOverride> { ["has_underscore"] = new() } },
            valid with { Limits = new PipelineLimits { MaxPages = 0 } },
            valid with { Limits = new PipelineLimits { MaxItems = PipelineConstants.MaxItemsCap + 1 } },
            valid with { Scope = new PipelineScope { Url = "ftp://example.com/list" } },
        ];

        foreach (var declaration in malformed)
            Error(await Broker.DeclarePipelineAsync(declaration)).Type.Should().Be(AppErrorType.Validation, declaration.ToString());
    }

    /// <summary>Saved filters and cadence can change without replacing a pipeline or its schedule history.</summary>
    [Fact]
    public async Task UpdatePipeline_ShouldKeepIdentityAndDisableOmittedKinds()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole);
        var original = Value(await Broker.DeclarePipelineAsync(Pipeline(container, source) with
        {
            Pinned = true,
            Schedules =
            [
                new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) },
                new() { Kind = WorkKind.Check, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(6) },
            ],
        }));
        var scope = new PipelineScope
        {
            Url = $"https://{source.Handle}/search?q=fashion",
            Parameters = new Dictionary<string, string> { ["city"] = "Samarqand" },
        };
        var updated = Value(await Broker.UpdatePipelineAsync(original.Id, new PipelineUpdate
        {
            Name = " Saved filter ", Scope = scope,
            Schedules = [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Cron, Cron = "0 9 * * *", TimeZone = "Asia/Tashkent", Enabled = false }],
        }));
        updated.Id.Should().Be(original.Id);
        updated.Pinned.Should().BeTrue();
        updated.SourceId.Should().Be(original.SourceId);
        updated.Name.Should().Be("Saved filter");
        updated.Scope.Should().BeEquivalentTo(scope);
        updated.Schedules.Select(row => row.Id).Should().BeEquivalentTo(original.Schedules.Select(row => row.Id));
        updated.Schedules.Should().OnlyContain(row => !row.Enabled);
        updated.Schedules.Single(row => row.Kind == WorkKind.Capture).Cron.Should().Be("0 9 * * *");
        Value(await Broker.GetPipelineAsync(original.Id)).Should().BeEquivalentTo(updated);
    }

    /// <summary>A conflicting normalized URL cannot overwrite another saved filter.</summary>
    [Fact]
    public async Task UpdatePipeline_ShouldRejectDuplicateScopesAndLeaveOriginalUnchanged()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole);
        var original = Value(await Broker.DeclarePipelineAsync(Pipeline(container, source)));
        Value(await Broker.DeclarePipelineAsync(Pipeline(container, source) with { Scope = new() { Url = $"https://{source.Handle}/search?a=1&b=2" } }));
        Error(await Broker.UpdatePipelineAsync(original.Id, new PipelineUpdate
        {
            Scope = new() { Url = $"https://{source.Handle}/search?b=2&a=1" }, Schedules = [],
        })).Type.Should().Be(AppErrorType.Conflict);
        Value(await Broker.GetPipelineAsync(original.Id)).Should().BeEquivalentTo(original);
    }

    /// <summary>Invalid cadence cannot partially save a URL edit.</summary>
    [Fact]
    public async Task UpdatePipeline_ShouldRejectInvalidCadenceAndUnknownIdentifiers()
    {
        var (container, source) = await SourceAsync(SourceCoverage.Whole);
        var original = Value(await Broker.DeclarePipelineAsync(Pipeline(container, source)));
        var update = new PipelineUpdate
        {
            Scope = new() { Url = $"https://{source.Handle}/filtered" },
            Schedules = [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(1) }],
        };
        Error(await Broker.UpdatePipelineAsync(original.Id, update)).Type.Should().Be(AppErrorType.Validation);
        Value(await Broker.GetPipelineAsync(original.Id)).Should().BeEquivalentTo(original);
        Error(await Broker.UpdatePipelineAsync(Guid.NewGuid(), update)).Type.Should().Be(AppErrorType.NotFound);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Returns twelve lowercase hex characters, unique per call.</summary>
    protected static string Unique() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Registers a fresh tenant.</summary>
    protected async Task<TenantModel> TenantAsync() =>
        Value(await Broker.RegisterTenantAsync(new TenantRegistration { Slug = $"tenant-{Unique()}", Name = "Tenant" }));

    /// <summary>Registers a private container for a tenant.</summary>
    /// <param name="tenant">The owner.</param>
    protected async Task<ContainerModel> PrivateContainerAsync(TenantModel tenant) =>
        Value(await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));

    /// <summary>Returns the instance's pool, registering it when it is missing.</summary>
    protected async Task<ContainerModel> PoolAsync()
    {
        if (await Broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool }) is Result<ContainerModel>.Success { Value: var pool })
            return pool;

        return Value(await Broker.ListContainersAsync()).Single(container => container.Kind == ContainerKind.Pool);
    }

    /// <summary>Builds a minimal source registration.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="family">The family.</param>
    /// <param name="handle">The handle.</param>
    protected static SourceRegistration Source(Guid containerId, SourceFamily family, string handle) => new()
    {
        ContainerId = containerId,
        Family = family,
        Handle = handle,
        Coverage = SourceCoverage.Whole,
        Completeness = SourceCompleteness.Complete,
        Posture = SourcePosture.OwnFetch,
    };

    /// <summary>Registers a fresh web source in a fresh private container.</summary>
    /// <param name="coverage">The source's coverage.</param>
    /// <param name="permission">The source's permission.</param>
    protected async Task<(ContainerModel Container, SourceModel Source)> SourceAsync(SourceCoverage coverage, SourcePermission permission = SourcePermission.Open)
    {
        var container = await PrivateContainerAsync(await TenantAsync());
        var source = Value(await Broker.RegisterSourceAsync(Source(container.Id, SourceFamily.Site, $"site-{Unique()}.example") with
        {
            Coverage = coverage,
            Permission = permission,
        }));
        return (container, source);
    }

    /// <summary>Builds a minimal pipeline declaration over a source.</summary>
    /// <param name="container">The container.</param>
    /// <param name="source">The source.</param>
    protected static PipelineDeclaration Pipeline(ContainerModel container, SourceModel source) =>
        new() { ContainerId = container.Id, SourceId = source.Id };

    /// <summary>Returns the success value, failing the test with the broker's own error otherwise.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="result">The broker's result.</param>
    protected static T Value<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    /// <summary>Returns the failure, failing the test when the broker succeeded.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="result">The broker's result.</param>
    protected static AppError Error<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Failure failure
            ? failure.Error
            : throw new Xunit.Sdk.XunitException($"Expected a failure, got {((Result<T>.Success)result).Value}.");
}
