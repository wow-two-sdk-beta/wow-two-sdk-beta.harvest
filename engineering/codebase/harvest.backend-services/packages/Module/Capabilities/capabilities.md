# Capabilities/

The inline capability layer — iteration 2, the fetch lane: direct fetching, per-host pacing, robots and challenge
detection.

| Implements (Abstractions) | Implementation |
|---|---|
| `Capabilities.Fetching.IFetchClientFactory` | direct clients over core `Http/Safety`; one honest, configurable user agent that names Harvest; a run-scoped anonymous cookie jar |
| `Capabilities.Fetching.IPageFetcher` | typed page responses with the same direct policy, explicit outcomes and crash-safe conditional replay |
| `Capabilities.Challenges.IChallengeDetector` | the generic detector: challenge, block or rate limit; adapters add their own |
| `Capabilities.Rendering.IBrowserRenderer` | declared, unbound; a step that needs it reports `capabilityUnavailable` |

- Owns: `harvest_host_state` (pacing, `Retry-After`, the robots cache), `harvest_host_lane` (each lane's pause,
  failures, last challenge and concurrency), `harvest_fetch_validators` (bounded public response validators and body).
  The engine owns lane pause state and request/byte attribution; fetching holds a PostgreSQL session advisory lock
  per IDNA host until each fetch finishes and updates the durable global gate.
- Rules: a challenge, block or rate limit is an outcome the run records; the lane pauses and its delay widens on each
  repeat, and a rate limit waits at least its `Retry-After`. Nothing retries past one through another route: no proxies
  or rotation, no disguised clients, no solvers, no sign-ins or accounts.
- Every fetch obeys the source's robots policy and sends conditional requests (`ETag`, `Last-Modified`); no generic HTTP
  resilience on fetch clients.
- Register explicitly with `AddHarvestFetching`, then bind `HarvestFetchOptions` in the host. The defaults identify
  `Harvest/0.1`, require a two-second gap and bound a response to 5 MiB. The configured user agent must name Harvest.
- Core's guarded transport checks every direct connection; redirects stay on the same host, cannot downgrade HTTPS,
  and cannot carry embedded credentials. Cookies stay in the caller's anonymous jar, never in a shared transport.
- Both host-state permission and the nearest matching operator host policy must allow a request before robots is read.
- A cached 304 carries its bounded prior body so a worker can replay content after a crash before ingest. Readers
  process that body and normal content hashes suppress unchanged revisions. A cookie jar, authentication, private or
  no-store responses, and varying request headers prevent shared conditional caching.
- `FetchResponse.Usage` counts transport attempts and bytes actually read, including robots and redirects; replaying
  a cached 304 adds no network bytes. A paused or disallowed request sends no transport attempt.
