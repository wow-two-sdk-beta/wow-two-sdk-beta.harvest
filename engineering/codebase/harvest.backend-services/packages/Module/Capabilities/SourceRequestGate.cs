using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Reuses the HTTP host store for provider SDK calls without replacing their transport.</summary>
internal static class SourceRequestGate
{
    internal static async Task<ISourceRequestLease> AcquireAsync(IHostFetchStore hosts, string host, TimeProvider clock,
        CancellationToken cancellationToken, TimeSpan? minimumDelay = null, bool queueAtHostPace = false)
    {
        var lease = await hosts.AcquireAsync(host, cancellationToken);
        try
        {
            var state = lease.State;
            if (state.Permission is not ("open" or "granted") || state.PolicyPermission is not ("open" or "granted"))
                return new RequestLease(lease, clock, TimeSpan.Zero, FetchOutcome.Restricted, null);
            var now = clock.GetUtcNow();
            var until = new[] { state.PausedUntil, state.RetryAfterUntil }.Max();
            if (until > now)
            {
                var outcome = Enum.TryParse<FetchOutcome>(state.PauseReason, out var previous)
                    && previous is not (FetchOutcome.Ok or FetchOutcome.Gone) ? previous : FetchOutcome.RateLimited;
                return new RequestLease(lease, clock, TimeSpan.Zero, outcome, until - now);
            }
            TimeSpan wait = state.NextAllowedAt - now;
            if (queueAtHostPace && wait > TimeSpan.Zero)
                return new RequestLease(lease, clock, TimeSpan.Zero, FetchOutcome.RateLimited, wait);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, clock, cancellationToken);
            var gap = TimeSpan.FromMilliseconds(Math.Max(state.MinimumDelayMilliseconds, state.AdaptiveDelayMilliseconds));
            if (state.CrawlDelaySeconds is { } crawl && TimeSpan.FromSeconds(crawl) > gap)
                gap = TimeSpan.FromSeconds(crawl);
            if (minimumDelay is { } required && required > gap)
                gap = required;
            state.NextAllowedAt = clock.GetUtcNow() + gap;
            await lease.SaveAsync(cancellationToken);
            return new RequestLease(lease, clock, gap, FetchOutcome.Ok, null);
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
    }

    private sealed class RequestLease(IHostFetchLease lease, TimeProvider clock, TimeSpan gap, FetchOutcome outcome,
        TimeSpan? retryAfter) : ISourceRequestLease
    {
        public FetchOutcome Outcome { get; } = outcome;
        public TimeSpan? RetryAfter { get; } = retryAfter;

        public Task CompleteAsync(FetchOutcome result, CancellationToken cancellationToken = default)
            => CompleteAsync(result, null, cancellationToken);

        public async Task CompleteAsync(FetchOutcome result, TimeSpan? cooldown, CancellationToken cancellationToken)
        {
            if (Outcome != FetchOutcome.Ok)
                return;
            if (result is FetchOutcome.Ok or FetchOutcome.Gone)
            {
                lease.State.ConsecutiveFailures = 0;
                lease.State.PauseReason = null;
                lease.State.PausedUntil = null;
                lease.State.RetryAfterUntil = null;
            }
            var now = clock.GetUtcNow();
            if (cooldown is { } delay && delay > TimeSpan.Zero)
            {
                var until = now + delay;
                if (lease.State.RetryAfterUntil is null || lease.State.RetryAfterUntil < until)
                    lease.State.RetryAfterUntil = until;
                lease.State.PauseReason = nameof(FetchOutcome.RateLimited);
            }
            lease.State.NextAllowedAt = now + gap;
            await lease.SaveAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }
}
