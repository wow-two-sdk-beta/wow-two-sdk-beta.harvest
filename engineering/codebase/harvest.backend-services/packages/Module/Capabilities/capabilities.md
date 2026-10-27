# Capabilities/

The inline capability layer — iteration 2, the fetch lane: direct fetching, per-host pacing, robots and challenge
detection.

| Implements (Abstractions) | Slice 1 |
|---|---|
| `Capabilities.Fetching.IFetchClientFactory` | direct clients over core `Http/Safety`; one honest, configurable user agent that names Harvest; a run-scoped anonymous cookie jar |
| `Capabilities.Challenges.IChallengeDetector` | the generic detector: challenge, block or rate limit; adapters add their own |
| `Capabilities.Rendering.IBrowserRenderer` | declared, unbound; a step that needs it reports `capabilityUnavailable` |

- Owns: `harvest_host_state` (pacing, `Retry-After`, the robots cache), `harvest_host_lane` (each lane's pause,
  failures, last challenge and concurrency), `harvest_usage` request and byte meters.
- Rules: a challenge, block or rate limit is an outcome the run records; the lane pauses and its delay widens on each
  repeat, and a rate limit waits at least its `Retry-After`. Nothing retries past one through another route: no proxies
  or rotation, no disguised clients, no solvers, no sign-ins or accounts.
- Every fetch obeys the source's robots policy and sends conditional requests (`ETag`, `Last-Modified`); no generic HTTP
  resilience on fetch clients.
