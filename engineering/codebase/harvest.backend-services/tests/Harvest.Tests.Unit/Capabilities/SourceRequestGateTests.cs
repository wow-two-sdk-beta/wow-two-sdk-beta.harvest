using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Unit.Capabilities;

public sealed class SourceRequestGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(2000, 5000, 5000)]
    [InlineData(10000, 5000, 10000)]
    public async Task Reservation_uses_the_longer_of_the_frozen_step_floor_and_host_pace(int hostDelay, int stepDelay, int expected)
    {
        var store = new Store();
        store.Lease.State.MinimumDelayMilliseconds = hostDelay;

        await using var reservation = await SourceRequestGate.AcquireAsync(store, "shared.example", new FixedClock(), default,
            TimeSpan.FromMilliseconds(stepDelay));

        reservation.Outcome.Should().Be(FetchOutcome.Ok);
        store.Lease.State.NextAllowedAt.Should().Be(Now.AddMilliseconds(expected));
    }

    [Fact]
    public async Task Reservation_is_saved_before_provider_work_and_success_clears_expired_host_failures()
    {
        var store = new Store();
        store.Lease.State.MinimumDelayMilliseconds = 5000;
        store.Lease.State.ConsecutiveFailures = 3;
        store.Lease.State.PausedUntil = Now.AddSeconds(-1);
        store.Lease.State.PauseReason = nameof(FetchOutcome.LoginRequired);

        await using var reservation = await SourceRequestGate.AcquireAsync(store, "www.instagram.com", new FixedClock(), default);

        reservation.Outcome.Should().Be(FetchOutcome.Ok);
        store.Host.Should().Be("www.instagram.com");
        store.Lease.Saves.Should().Be(1);
        store.Lease.State.NextAllowedAt.Should().Be(Now.AddSeconds(5));
        await reservation.CompleteAsync(FetchOutcome.Ok);
        store.Lease.State.ConsecutiveFailures.Should().Be(0);
        store.Lease.State.PauseReason.Should().BeNull();
        store.Lease.State.PausedUntil.Should().BeNull();
        store.Lease.State.MinimumDelayMilliseconds.Should().Be(5000);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_host_or_operator_denial_blocks_without_reserving_a_request(bool policy)
    {
        var store = new Store();
        if (policy) store.Lease.State.PolicyPermission = "denied";
        else store.Lease.State.Permission = "denied";

        await using (var reservation = await SourceRequestGate.AcquireAsync(store, "shared.example", new FixedClock(), default))
        {
            reservation.Outcome.Should().Be(FetchOutcome.Restricted);
            await reservation.CompleteAsync(FetchOutcome.Ok);
            store.Lease.Saves.Should().Be(0);
        }
        store.Lease.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_retry_after_pause_is_reported_without_waiting_or_reserving()
    {
        var store = new Store();
        store.Lease.State.PausedUntil = Now.AddMinutes(1);
        store.Lease.State.RetryAfterUntil = Now.AddMinutes(2);
        store.Lease.State.PauseReason = nameof(FetchOutcome.RateLimited);

        await using var reservation = await SourceRequestGate.AcquireAsync(store, "shared.example", new FixedClock(), default);

        reservation.Outcome.Should().Be(FetchOutcome.RateLimited);
        reservation.RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
        store.Lease.Saves.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_during_pacing_releases_the_host_without_moving_its_reservation()
    {
        var store = new Store();
        store.Lease.State.NextAllowedAt = Now.AddMinutes(1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> acquire = async () => await SourceRequestGate.AcquireAsync(store, "shared.example", new FixedClock(), cancellation.Token);

        await acquire.Should().ThrowAsync<OperationCanceledException>();
        store.Lease.Disposed.Should().BeTrue();
        store.Lease.Saves.Should().Be(0);
        store.Lease.State.NextAllowedAt.Should().Be(Now.AddMinutes(1));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Store : IHostFetchStore
    {
        internal Lease Lease { get; } = new();
        internal string? Host { get; private set; }
        public Task<IHostFetchLease> AcquireAsync(string host, CancellationToken cancellationToken)
        {
            Host = host;
            return Task.FromResult<IHostFetchLease>(Lease);
        }
    }
    private sealed class Lease : IHostFetchLease
    {
        public HostFetchState State { get; } = new() { NextAllowedAt = Now };
        internal int Saves { get; private set; }
        internal bool Disposed { get; private set; }
        public Task SaveAsync(CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
        public Task<ResponseValidators?> GetValidatorsAsync(string url, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveValidatorsAsync(string url, ResponseValidators validators, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
