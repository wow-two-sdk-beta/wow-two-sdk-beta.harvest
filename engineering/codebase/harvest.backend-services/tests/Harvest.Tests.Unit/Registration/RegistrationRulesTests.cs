using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Unit.Registration;

/// <summary>The registry's pure rules: normalization of slugs, handles and scopes, and the shape of schedules, steps and
/// limits. Store-backed rules are conformance's.</summary>
public sealed class RegistrationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(" Events ", "events")]
    [InlineData("haven-supply", "haven-supply")]
    [InlineData("a1", "a1")]
    public void NormalizeSlug_ShouldTrimAndLowercase_WhenTheSlugIsWellFormed(string slug, string expected)
    {
        RegistrationRules.NormalizeSlug(slug).Slug.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("trailing-")]
    [InlineData("dot.ted")]
    public void NormalizeSlug_ShouldFail_WhenTheSlugIsMalformed(string slug)
    {
        RegistrationRules.NormalizeSlug(slug).Error.Should().NotBeNull();
    }

    [Fact]
    public void NormalizeSlug_ShouldFail_WhenTheSlugIsTooLong()
    {
        RegistrationRules.NormalizeSlug(new string('a', 64)).Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData(SourceFamily.Site, "https://Afisha.UZ/ru/events?x=1", "afisha.uz")]
    [InlineData(SourceFamily.Feed, "mstar.uz/feed.xml", "mstar.uz")]
    [InlineData(SourceFamily.Listing, "olx.uz:443", "olx.uz")]
    [InlineData(SourceFamily.Page, "пример.рф", "xn--e1afmkfd.xn--p1ai")]
    [InlineData(SourceFamily.Telegram, "https://t.me/s/Uybor/120", "tg:uybor")]
    [InlineData(SourceFamily.Telegram, "tg:afishauz", "tg:afishauz")]
    [InlineData(SourceFamily.Instagram, "@Ocharo.Brand", "ocharo.brand")]
    public void NormalizeHandle_ShouldKeepTheFamilysIdentity_WhenTheHandleIsWellFormed(SourceFamily family, string handle, string expected)
    {
        RegistrationRules.NormalizeHandle(family, handle).Handle.Should().Be(expected);
    }

    [Theory]
    [InlineData(SourceFamily.Site, "not a host")]
    [InlineData(SourceFamily.Telegram, "@abc")]
    [InlineData(SourceFamily.X, "two words")]
    [InlineData(SourceFamily.Page, "   ")]
    public void NormalizeHandle_ShouldFail_WhenTheHandleIsMalformed(SourceFamily family, string handle)
    {
        RegistrationRules.NormalizeHandle(family, handle).Error.Should().NotBeNull();
    }

    [Fact]
    public void CanonicalScopeKey_ShouldBeTheWholeSourceKey_WhenTheScopeIsEmpty()
    {
        RegistrationRules.CanonicalScopeKey(null).ScopeKey.Should().Be(PipelineConstants.WholeScopeKey);
        RegistrationRules.CanonicalScopeKey(new PipelineScope()).ScopeKey.Should().Be(PipelineConstants.WholeScopeKey);
    }

    [Fact]
    public void CanonicalScopeKey_ShouldSortTheQueryAndParametersAndDropTheFragment_WhenAUrlAndParametersAreGiven()
    {
        var key = RegistrationRules.CanonicalScopeKey(new PipelineScope
        {
            Url = "HTTPS://OLX.uz:443/list?b=2&a=1#top",
            Parameters = new Dictionary<string, string> { ["z"] = " last ", ["city"] = "tashkent" },
        }).ScopeKey;

        key.Should().Be("url=" + Uri.EscapeDataString("https://olx.uz/list?a=1&b=2") + "&city=tashkent&z=last");
    }

    [Theory]
    [InlineData("ftp://example.com/x")]
    [InlineData("/relative/path")]
    public void CanonicalScopeKey_ShouldFail_WhenTheUrlIsNotAbsoluteHttp(string url)
    {
        RegistrationRules.CanonicalScopeKey(new PipelineScope { Url = url }).Error.Should().NotBeNull();
    }

    [Fact]
    public void ValidateSchedules_ShouldAcceptOneRowPerKind_WhenEachModeHasItsSpec()
    {
        RegistrationRules.ValidateSchedules(
        [
            new ScheduleDeclaration { Kind = WorkKind.Discover, Mode = ScheduleMode.Interval, Every = TimeSpan.FromMinutes(5) },
            new ScheduleDeclaration { Kind = WorkKind.Refresh, Mode = ScheduleMode.Cron, Cron = "0 3 * * 1", TimeZone = "UTC" },
            new ScheduleDeclaration { Kind = WorkKind.Check, Mode = ScheduleMode.Adaptive, Every = TimeSpan.FromHours(1), MaxEvery = TimeSpan.FromDays(1) },
            new ScheduleDeclaration { Kind = WorkKind.Metrics, Mode = ScheduleMode.Manual },
        ]).Should().BeNull();
    }

    [Fact]
    public void ValidateSchedules_ShouldFail_WhenAnIntervalCarriesACron()
    {
        RegistrationRules.ValidateSchedules(
            [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1), Cron = "* * * * *" }])
            .Should().NotBeNull();
    }

    [Fact]
    public void ValidateSchedules_ShouldFail_WhenTheWindowOpensAndClosesTogether()
    {
        RegistrationRules.ValidateSchedules(
            [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Manual, Window = new ScheduleWindow { Start = new TimeOnly(2, 0), End = new TimeOnly(2, 0) } }])
            .Should().NotBeNull();
    }

    [Fact]
    public void ValidateStepOverrides_ShouldAcceptALaneAndACap_WhenBothAreWellFormed()
    {
        RegistrationRules.ValidateStepOverrides(new Dictionary<string, StepOverride>
        {
            ["contact"] = new() { Enabled = true, Lane = "olx-contact", MaxUsdPerMonth = 5 },
        }).Should().BeNull();
    }

    [Fact]
    public void ValidateStepOverrides_ShouldFail_WhenTheMonthlyCapIsNegative()
    {
        RegistrationRules.ValidateStepOverrides(new Dictionary<string, StepOverride> { ["detail"] = new() { MaxUsdPerMonth = -1 } })
            .Should().NotBeNull();
    }

    [Fact]
    public void ValidateStepOverrides_ShouldFail_WhenTheLaneIsMalformed()
    {
        RegistrationRules.ValidateStepOverrides(new Dictionary<string, StepOverride> { ["detail"] = new() { Lane = "Lane One" } })
            .Should().NotBeNull();
    }

    [Fact]
    public void ValidateLimits_ShouldFail_WhenABudgetIsNotPositive()
    {
        RegistrationRules.ValidateLimits(new PipelineLimits { MaxDuration = TimeSpan.Zero }).Should().NotBeNull();
        RegistrationRules.ValidateLimits(new PipelineLimits { MaxProbes = -1 }).Should().NotBeNull();
        RegistrationRules.ValidateLimits(new PipelineLimits { MaxPages = 100, MaxItems = 2000 }).Should().BeNull();
    }

    [Fact]
    public void ValidateCapabilityPolicy_ShouldFail_WhenACapIsNegativeOrALaneMalformed()
    {
        RegistrationRules.ValidateCapabilityPolicy(new CapabilityPolicy { MonthUsd = -1 }).Should().NotBeNull();
        RegistrationRules.ValidateCapabilityPolicy(new CapabilityPolicy { Defaults = new StepCapabilityPolicy { MaxUsdPerMonth = -1 } })
            .Should().NotBeNull();
        RegistrationRules.ValidateCapabilityPolicy(new CapabilityPolicy
        {
            Steps = new Dictionary<string, StepCapabilityPolicy> { ["detail"] = new() { Lane = "Lane One" } },
        }).Should().NotBeNull();
        RegistrationRules.ValidateCapabilityPolicy(new CapabilityPolicy
        {
            Defaults = new StepCapabilityPolicy { MaxUsdPerMonth = 10 },
            Steps = new Dictionary<string, StepCapabilityPolicy> { ["contact"] = new() { Lane = "contact", MaxUsdPerMonth = 3 } },
            MonthUsd = 30,
        }).Should().BeNull();
    }

    [Fact]
    public void ValidateApiKeyIssue_ShouldAcceptEveryTenantScope_WhenNoOperatorScopeIsAsked()
    {
        RegistrationRules.ValidateApiKeyIssue(new ApiKeyIssue { Name = "all", Scopes = HarvestScopeConstants.TenantScopes }, Now).Should().BeNull();
    }

    [Fact]
    public void ValidateContainerOwner_ShouldFail_WhenThePoolNamesATenant()
    {
        RegistrationRules.ValidateContainerOwner(new ContainerRegistration { Kind = ContainerKind.Pool, TenantId = Guid.NewGuid() })
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData(SourcePermission.Open, PipelineState.Active)]
    [InlineData(SourcePermission.Granted, PipelineState.Active)]
    [InlineData(SourcePermission.Restricted, PipelineState.PendingApproval)]
    public void InitialState_ShouldFollowThePermission_WhenAPipelineIsDeclared(SourcePermission permission, PipelineState expected)
    {
        PipelineRules.InitialState(permission).Should().Be(expected);
    }
}
