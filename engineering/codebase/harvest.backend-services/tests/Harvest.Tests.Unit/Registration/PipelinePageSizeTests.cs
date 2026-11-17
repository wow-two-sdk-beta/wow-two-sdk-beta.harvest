using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Unit.Registration;

public sealed class PipelinePageSizeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(1000)]
    public void Generic_page_size_accepts_optional_bounded_requests(int? pageSize)
    {
        RegistrationRules.ValidateLimits(new PipelineLimits { PageSize = pageSize }).Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1001)]
    public void Invalid_page_size_is_rejected_when_declared_or_updated(int pageSize)
    {
        var stored = new PipelineLimits { PageSize = 25, MaxPages = 3, MaxItems = 100 };
        RegistrationRules.ValidateLimits(new PipelineLimits { PageSize = pageSize })?.Message
            .Should().Be("PageSize must be 1–1000.");

        var update = RegistrationRules.MergeLimits(stored, new PipelineLimits { PageSize = pageSize });

        update.Error.Should().NotBeNull();
        update.Limits.Should().Be(stored);
    }

    [Fact]
    public void Page_size_update_keeps_other_limits_and_omitting_it_keeps_the_saved_value()
    {
        var stored = new PipelineLimits { PageSize = 25, MaxPages = 3, MaxItems = 100 };

        var update = RegistrationRules.MergeLimits(stored, new PipelineLimits { PageSize = 10 });

        update.Error.Should().BeNull();
        update.Limits.Should().Be(stored with { PageSize = 10 });
        var cadenceOnly = RegistrationRules.MergeLimits(update.Limits, new PipelineLimits { MaxPages = 2 });
        cadenceOnly.Error.Should().BeNull();
        cadenceOnly.Limits.Should().Be(stored with { PageSize = 10, MaxPages = 2 });
    }

    [Fact]
    public async Task Fake_registry_matches_page_size_persistence_merge_and_refusal()
    {
        var registry = new FakeRegistrationBroker();
        var tenant = Value(await registry.RegisterTenantAsync(new() { Slug = "page-size", Name = "Page size" }));
        var container = Value(await registry.RegisterContainerAsync(new() { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await registry.RegisterSourceAsync(new()
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = "example.test",
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
        }));
        var declaration = new PipelineDeclaration
        {
            ContainerId = container.Id, SourceId = source.Id, Scope = new() { Url = "https://example.test/catalog" },
            Limits = new() { PageSize = 25, MaxPages = 3 },
        };
        (await registry.DeclarePipelineAsync(declaration with { Limits = new() { PageSize = 1001 } }))
            .Should().BeOfType<Result<PipelineModel>.Failure>();
        var pipeline = Value(await registry.DeclarePipelineAsync(declaration));
        var update = new PipelineUpdate { Scope = pipeline.Scope, Schedules = [], Limits = new() { PageSize = 10 } };
        Value(await registry.UpdatePipelineAsync(pipeline.Id, update)).Limits.Should().Be(new PipelineLimits { PageSize = 10, MaxPages = 3 });
        Value(await registry.UpdatePipelineAsync(pipeline.Id, update with { Limits = new() { MaxPages = 2 } }))
            .Limits.Should().Be(new PipelineLimits { PageSize = 10, MaxPages = 2 });
        (await registry.UpdatePipelineAsync(pipeline.Id, update with { Limits = new() { PageSize = 0 } }))
            .Should().BeOfType<Result<PipelineModel>.Failure>();
        Value(await registry.GetPipelineAsync(pipeline.Id)).Limits.PageSize.Should().Be(10);
    }

    private static T Value<T>(Result<T> result) where T : notnull => Assert.IsType<Result<T>.Success>(result).Value;
}
