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

## Built — iteration 2 (fetching)

| Piece | File |
|---|---|
| `IFetchClientFactory`: direct `SocketsHttpHandler`, one connection per host, no redirects/proxy, the operator UA, a run's cookie jar, core's address validator | `Fetching/FetchClientFactory.cs`, `Fetching/FetchingOptions.cs` |
| `IChallengeDetector`: the generic challenge / block / rate-limit classifier, first verdict wins | `Challenges/GenericChallengeDetector.cs` |
| robots parse + allow/deny + crawl delay | `Robots/RobotsFile.cs` |
| per-host pacing + back-off math (2 s floor, 30 s crawl ceiling, `Retry-After`, widening pause) | `Pacing/HostPacing.cs` |
| `Retry-After` reading (seconds or HTTP-date) | `Fetching/RetryAfterPolicy.cs` |
| the host/lane read-write over `harvest_host_state` and `harvest_host_lane` | `HostStateStore.cs`, `Persistence/Rows/Host{State,Lane}Row.cs` |
| `IPoliteFetcher`: paces, obeys robots, sends conditionals, answers 304 as not-modified, honours `Retry-After`, classifies, records the host's back-off | `PoliteFetcher.cs`, `FetchRequest.cs`, `FetchResult.cs` |

- The orchestration is Module-internal; the Engine (iteration 3) drives `IPoliteFetcher` and records the outcome on the
  run. A block, a challenge or a rate limit is recorded and backs the host off — never worked around.
