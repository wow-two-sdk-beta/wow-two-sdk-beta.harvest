using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace HarvestAdapters.Tests.Unit.Instagram;

public sealed class InstagramStepTests
{
    [Fact]
    public async Task Pagination_is_bounded_and_never_claims_complete_enumeration()
    {
        var client = new FixtureClient();
        var broker = new InstagramStepBroker(client, Options.Create(InstagramClientTests.Settings()));
        var context = new Context();
        var first = await broker.ExecuteAsync(Item("https://www.instagram.com/teplofest/"), context);
        first.Captures.Should().ContainSingle();
        first.EnumerationComplete.Should().BeFalse();
        first.Next.Should().ContainSingle();
        first.Next.Single().Target.Should().StartWith("https://www.instagram.com/teplofest/?__harvest_cursor=");
        first.Next.Single().Target.Should().NotContain("opaque-cursor");
        var continuation = first.Next.Single();
        var last = await broker.ExecuteAsync(Item(continuation.Target) with { Payload = continuation.Payload }, context);
        last.Next.Should().BeEmpty();
        last.Message.Should().Contain("page limit");
        last.EnumerationComplete.Should().BeFalse();
        client.Calls.Should().Equal(("teplofest", (string?)null), ("teplofest", "opaque-cursor"));
        context.Signals.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("https://www.instagram.com/p/ABC/")]
    [InlineData("https://example.test/teplofest/")]
    [InlineData("https://user:password@www.instagram.com/teplofest/")]
    public async Task Non_profile_targets_do_not_call_the_provider(string target)
    {
        var client = new FixtureClient();
        await new InstagramStepBroker(client, Options.Create(InstagramClientTests.Settings())).ExecuteAsync(Item(target), new Context());
        client.Calls.Should().BeEmpty();
    }

    private static StepItem Item(string target) => new()
    {
        WorkItemId = Guid.NewGuid(), RunId = Guid.NewGuid(), PipelineId = Guid.NewGuid(), ContainerId = Guid.NewGuid(),
        SourceId = Guid.NewGuid(), StepKey = "enumerate", HostKey = "www.instagram.com", Lane = "direct", Target = target,
    };
    private sealed class FixtureClient : IInstagramMediaClient
    {
        internal List<(string, string?)> Calls { get; } = [];
        public Task<InstagramMediaPage> ReadAsync(string username, string? after = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((username, after));
            return Task.FromResult(new InstagramMediaPage
            {
                Outcome = FetchOutcome.Ok, After = "opaque-cursor",
                Media = [InstagramCaptureTests.Media("Join us at the fashion market", 1)], Usage = new() { Requests = 1, Bytes = 100 },
            });
        }
    }
    private sealed class Context : IStepContext
    {
        internal List<StepSignal> Signals { get; } = [];
        public ResolvedPlan Plan => throw new NotSupportedException();
        public SourceModel Source => throw new NotSupportedException();
        public HttpClient Http => throw new NotSupportedException();
        public IBrowserRenderer? Browser => null;
        public IChallengeDetector Challenges => throw new NotSupportedException();
        public TimeProvider Clock => TimeProvider.System;
        public void Observe(StepSignal signal) => Signals.Add(signal);
    }
}
