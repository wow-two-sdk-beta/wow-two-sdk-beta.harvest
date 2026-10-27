using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Readers;

/// <summary>Checks that generic steps preserve fetch outcomes and never turn refusals into deletion evidence.</summary>
public sealed class GenericStepBrokerTests
{
    [Theory]
    [InlineData(FetchOutcome.Challenge)]
    [InlineData(FetchOutcome.Blocked)]
    [InlineData(FetchOutcome.RateLimited)]
    [InlineData(FetchOutcome.RobotsDisallowed)]
    public async Task Check_ShouldKeepRefusalOutcomesAndNeverEmitDeletion(FetchOutcome outcome)
    {
        var fetcher = new Fetcher(new() { Outcome = outcome, RetryAfter = TimeSpan.FromMinutes(3) });
        var context = new Context();
        var result = await new GenericStepBroker("generic-page", "check", fetcher, []).ExecuteAsync(Item(), context);
        result.Outcome.Should().Be(outcome);
        result.Links.Should().BeEmpty();
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(3));
        context.Signals.Should().ContainSingle();
        fetcher.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Check_ShouldEmitStrongEvidenceOnlyForDirectMissingResponses(int status)
    {
        var document = CaptureReaderTests.Document(string.Empty) with { StatusCode = status };
        var fetcher = new Fetcher(new() { Outcome = FetchOutcome.Gone, Document = document });
        var result = await new GenericStepBroker("generic-page", "check", fetcher, []).ExecuteAsync(Item(), new Context());
        result.Links.Should().ContainSingle();
        result.Links[0].ExternalKey.Should().Be("stable-id");
        result.Links[0].Signal.Should().Be(LinkSignal.StrongNegative);
        result.Links[0].Reason.Should().Be($"direct_{status}");
    }

    [Fact]
    public async Task Enumerate_ShouldKeepOnlySameHostSitemapTargets()
    {
        var fetcher = new Fetcher(new()
        {
            Outcome = FetchOutcome.Ok,
            Document = CaptureReaderTests.Document("<urlset><url><loc>https://example.com/a</loc></url><url><loc>https://other.example/b</loc></url></urlset>", contentType: "application/xml"),
        });
        var result = await new GenericStepBroker("generic-site", "enumerate", fetcher, [new SitemapCaptureReader()])
            .ExecuteAsync(Item(), new Context());
        result.Next.Should().ContainSingle();
        result.Next[0].Target.Should().Be("https://example.com/a");
        result.Next[0].StepKey.Should().Be("detail");
    }

    [Fact]
    public async Task Detail_ShouldParseCached304ContentAfterAnInterruptedCapture()
    {
        var fetcher = new Fetcher(new()
        {
            Outcome = FetchOutcome.Ok, NotModified = true,
            Document = CaptureReaderTests.Document("<h1>Recovered</h1><p>Body</p>") with { StatusCode = 304 },
        });
        var result = await new GenericStepBroker("generic-page", "detail", fetcher, [new PageCaptureReader()]).ExecuteAsync(Item(), new Context());
        result.Captures.Should().ContainSingle();
        result.Captures[0].ContentProjection.Should().Contain("Recovered");
    }

    [Fact]
    public async Task Detail_ShouldMeterTransportUsageRatherThanTheReplayedBodySize()
    {
        var fetcher = new Fetcher(new()
        {
            Outcome = FetchOutcome.Ok, NotModified = true, Usage = new() { Requests = 2, Bytes = 17 },
            Document = CaptureReaderTests.Document("<h1>Cached content is longer than the new bytes</h1>") with { StatusCode = 304 },
        });
        var context = new Context();
        var result = await new GenericStepBroker("generic-page", "detail", fetcher, [new PageCaptureReader()]).ExecuteAsync(Item(), context);
        result.Usage.Requests.Should().Be(2);
        result.Usage.Bytes.Should().Be(17);
        context.Signals.Single().Bytes.Should().Be(17);
    }

    [Fact]
    public async Task Detail_ShouldReportJavascriptShellInsteadOfCreatingAnEmptyPost()
    {
        var fetcher = new Fetcher(new()
        {
            Outcome = FetchOutcome.Ok, Document = CaptureReaderTests.Document("<div id='app'></div><script src='/app.js'></script>"),
        });
        var result = await new GenericStepBroker("generic-page", "detail", fetcher, [new PageCaptureReader()]).ExecuteAsync(Item(), new Context());
        result.Outcome.Should().Be(FetchOutcome.RenderRequired);
        result.Captures.Should().BeEmpty();
    }

    private static StepItem Item() => new()
    {
        WorkItemId = Guid.NewGuid(), RunId = Guid.NewGuid(), PipelineId = Guid.NewGuid(), ContainerId = Guid.NewGuid(),
        SourceId = Guid.NewGuid(), StepKey = "check", Lane = "default", HostKey = "example.com", Target = "https://example.com/page",
        Payload = JsonSerializer.SerializeToElement(new { externalKey = "stable-id" }),
    };

    private sealed class Fetcher(FetchResponse response) : IPageFetcher
    {
        public int Calls { get; private set; }
        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(response);
        }
    }

    private sealed class Context : IStepContext
    {
        public List<StepSignal> Signals { get; } = [];
        public ResolvedPlan Plan { get; } = new() { StepKey = "check", Lane = "default", HostKey = "example.com", Requirements = new() };
        public SourceModel Source { get; } = new()
        {
            Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Page, Handle = "example.com",
            Coverage = default, Completeness = default, Posture = default, Permission = default,
            AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, ConsentBasis = default,
            Authority = 50, RetentionClass = "default", CapabilityPolicy = new(), State = default,
            StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
        };
        public HttpClient Http => throw new InvalidOperationException("The broker must use its policy-aware fetcher.");
        public IBrowserRenderer? Browser => null;
        public IChallengeDetector Challenges => throw new InvalidOperationException();
        public TimeProvider Clock => TimeProvider.System;
        public void Observe(StepSignal signal) => Signals.Add(signal);
    }
}
