# wow-two-sdk-beta.harvest — Contract

*Last updated: 2026-10-27*

> Everything in `packages/Abstractions`: the registry broker every form implements, and the seams the engine lanes and
> source adapters build against. Interfaces and models only — no DI, options or I/O. Rules:
> wow-two-ws/conventions/development/capability-forms.md § Contract.

---

## Registration

`IRegistrationBroker` (`Registration`) — tenants, their API keys, containers, sources and pipelines.

| Member | Returns | Failures |
|---|---|---|
| `RegisterTenantAsync(TenantRegistration)` | `TenantModel`; slug trimmed and lowercased | `Validation` slug or name · `Conflict` taken slug |
| `GetTenantAsync(id)` · `ListTenantsAsync()` | the tenant · every tenant by slug | `NotFound` |
| `IssueApiKeyAsync(tenantId, ApiKeyIssue)` | `IssuedApiKeyModel`: the secret once, `hv_` + 32; only its SHA-256 is kept | `NotFound` tenant · `Validation` name, operator or unknown scope, past expiry |
| `GetApiKeyAsync(keyId)` · `ListApiKeysAsync(tenantId)` | metadata and prefix, never the secret | `NotFound` |
| `RevokeApiKeyAsync(tenantId, keyId)` | the key, revoked; idempotent | `NotFound` when the tenant holds no such key |
| `RegisterContainerAsync(ContainerRegistration)` | the pool (no tenant) or a private container (one tenant) | `Validation` owner · `NotFound` tenant · `Conflict` second pool, taken external ref |
| `GetContainerAsync(id)` · `ListContainersAsync(tenantId?)` | one · a tenant's or every container | `NotFound` |
| `RegisterSourceAsync(SourceRegistration)` | `SourceModel`; handle normalized per family, permissive posture defaults | `NotFound` container · `Validation` handle, authority, retention class, check policy, capability policy · `Conflict` handle |
| `GetSourceAsync(id)` · `ListSourcesAsync(containerId)` | one · by family and handle | `NotFound` |
| `DeclarePipelineAsync(PipelineDeclaration)` | `PipelineModel` with its schedule rows; `active` over an open or granted source, else `pendingApproval` | `NotFound` container, source · `Validation` source of another container, denied source, filtered source without scope, schedules, step overrides, limits · `Conflict` same `(container, source, scope key)` |
| `GetPipelineAsync(id)` · `ListPipelinesAsync(containerId)` | one · oldest first | `NotFound` |

- Normalization: web handles reduce to their IDNA host; Telegram handles become `tg:{username}`; the scope key is `*`
  for the whole source, else the normalized URL (sorted query, no fragment) and the parameters sorted by name.
- Posture switches ship permissive (`permission` open, `aiAllowed`, `poolShareable`, `robotsPolicy` obey); a ban or a
  takedown is one row's update. `posture` is required and gates nothing.
- Schedules: one row per kind of `capture` · `discover` · `refresh` · `check` · `metrics` (a sweep is three rows);
  `interval` ≥ 5 min, `cron` five or six fields, `adaptive` min and max, IANA zone (default `Asia/Tashkent`), optional
  daily window. `nextDueAtUtc` stays null until the scheduler computes it.
- Step overrides and capability policies set a step's lane and monthly cap (never negative); a policy adds the source's
  monthly cap. Step keys and lanes match `StepConstants.KeyPattern`.

| Form | Implementation | Wire |
|---|---|---|
| 1 | `ModuleRegistrationBroker` (Module) | in process, over the `harvest_` registry tables |
| 2/3 | `ApiRegistrationBroker` (Client) | `RegistrationRouteConstants` routes, `{ data }` envelope, key in `X-Api-Key`; 400 → `Validation`, 401, 403, 404, 409, else `ExternalUnavailable` |
| fe | `createRegistryClient` (`fe-harvest/kit`) | the read routes: containers, sources, pipelines |

- Conformance: `RegistrationBrokerConformance` (`packages/Testing`) runs against the module, the client (in-process
  host, or `CONFORMANCE_BASE_URL` with `CONFORMANCE_API_KEY`) and `FakeRegistrationBroker` — `tests/Harvest.Tests.Conformance`.

---

## Access

| Route | Policy | A tenant key sees |
|---|---|---|
| `api/tenants` · `api/tenants/{id}/api-keys` · `api/api-keys/{id}` | `tenants:manage` | nothing (403) |
| `POST api/containers` · `POST api/sources` | `containers:manage` · `sources:manage` | nothing (403) |
| `GET api/containers` · `GET api/sources` | any live key | the pool and its own tenant's |
| `POST api/pipelines` | `pipelines:manage` | its own private containers; the pool is 403 |
| `GET api/pipelines` | `pipelines:read` | the pool and its own tenant's; another tenant's is 404 |

- Keys: core `Identity/ApiKeys` (`ApiKey` scheme, gate on `/api`, no local pass); `/api/system/status` stays open.
- The operator key (`Harvest:Operator:ApiKey`, `HarvestModuleOptions.OperatorApiKey`) holds every scope and no tenant;
  it comes from a host file or the vault. Tenant keys hold `HarvestScopeConstants.TenantScopes` only.

---

## Engine seams

The lanes of v0.1 implement these in `packages/Module/{Lane}/`; named-source brokers implement the step contract in
the private adapters repository. Every type references the BCL and the kernel only.

| Seam | Namespace | Implemented by | Called by |
|---|---|---|---|
| `ISourceAdapter` → `AdapterDescriptor` (`StepDescriptor`, plans per `WorkKind`) | `Steps` | Readers lane (generic family adapters) · private adapters repo (named sources) | Engine |
| `IStepBroker.ExecuteAsync(StepItem, IStepContext)` → `StepResult` | `Steps` | the same adapters, one broker per step | Engine |
| `IStepContext` (`ResolvedPlan`, `Observe(StepSignal)`) | `Steps` | Engine lane (iteration 3) | step brokers |
| `StepRequirements` (`RenderNeed`, `MinDelay`) | `Steps` | vocabulary | adapters, Engine |
| outcomes: `FetchOutcome`, `ItemOutcome`, `LinkSignal` · `LinkObservation`, `RunReason` | `Steps` · `Runs` | vocabulary | adapters, Engine, Posts |
| `IFetchClientFactory` — direct clients, one user agent, a run's cookie jar | `Capabilities.Fetching` | Fetching lane (iteration 2), inline in `Module/Capabilities/` over core `Http/Safety` | Engine |
| `IChallengeDetector` (`ChallengeProbe` → `ChallengeVerdict`: challenge, block or rate limit; `ChallengeKind`) | `Capabilities.Challenges` | Fetching lane (generic) · adapters (their own) | step brokers |
| `IBrowserRenderer` | `Capabilities.Rendering` | declared, unbound in v0.1; a step that needs it reports `capabilityUnavailable` | Engine |
| `ICaptureReader` → `CaptureReadResult` (`Capture`, `DiscoveredUrl`) over `FetchedDocument` | `Readers` | Readers lane (iteration 4) | step brokers |
| `IPostExtractor` → `PostEnvelope` (`wow2.post.v1`) | `Extraction` | Extraction lane (iteration 5) | Engine |
| `PostState` · `LinkState` · `LinkRole` | `Posts` | Posts lane (iteration 6) over its tables | Delivery, console |
| `SubscriptionModel` · `DeliveryEvent` · `DeliveryEventTypes` · `CursorPage<T>` | `Delivery` | Delivery lane (iteration 7), which declares the subscription broker | consumers |

- No escalation: a challenge, a block or a rate limit is a `FetchOutcome` the run records, and the host backs off.
  Nothing retries it through another route — no proxy or rotation, no disguised client, no solver, no sign-in or account.
- The renderer serves pages that show their content only through JavaScript, never a challenge.
- The schema in the SQL migrations (`001` baseline, `002` without the ladder) is the shared seam between lanes: each
  lane maps the tables its folder's lead doc names.
- `Capture.ContentProjection` drives the content hash; a capture row exists only when it changes.

---

## Store

- PostgreSQL 16+, UTF-8, `pg_trgm`. `MigrateHarvestOnStartupAsync` creates the database, refuses a non-UTF-8 one or one
  without `pg_trgm`, applies the migration set `harvest` (history `harvest_migration_history`, own advisory lock), and
  refuses a database whose `show_trgm('привет')` is empty.
- Tables carry the `harvest_` prefix in `public`, beside a Form 1 consumer's own; the module keeps its own keyed Npgsql
  data source (`harvest`). `harvest_captures` is monthly-partitioned with a default partition;
  `harvest_ensure_capture_partitions(n)` creates months ahead.

---

## Runs, posts and pull delivery

- `IRunBroker`: `StartAsync`, `GetAsync`, `ListAsync`, `CancelAsync`, `SetPausedAsync`, `HostsAsync`.
  Routes: `api/runs`, `api/runs/{id}/cancel`, `api/pipelines/{id}/pause`, `api/hosts?containerId`.
- `IPostBroker`: `SaveCaptureAsync`, `ObserveLinkAsync`, `GetPostAsync`, `ListPostsAsync`, `MergeAsync`, `UnmergeAsync`.
  Routes: `api/posts`, operator-only `captures`/`observations`, `api/post-merges`.
- `IDeliveryBroker`: `SubscribeAsync`, `GetSubscriptionAsync`, `ListSubscriptionsAsync`, `PullAsync`.
  Routes: `api/subscriptions`, `api/subscriptions/{id}/events`.

- all successful routes use the same `{ data }` envelope, including `{ data: { data, next, hasMore } }` for pull pages
- queueing joins an active run of the same pipeline and work kind; only active, permitted pipelines queue work
- runs, posts and host queues read inside visible containers; tenant run/pause controls require their own private container
- subscription creation verifies the tenant and readable container; subscription reads and pulls require its owning tenant
- HTTP capture/observation/merge controls require operator scope; no tenant can ingest into or merge the shared pool
- a hash-identical capture creates no new capture or delivery revision; replayed negative observations count once per run
- 404/410 evidence follows the source's check policy; challenges and rate limits never establish disappearance
- merges retain an undo record; undo restores moved links and prevents the separated posts from being auto-merged again
- delivery stores immutable revision snapshots; `wow2.event.v1` selects the event projection while retaining post identity and provenance
- pull cursors contain the subscription and `(transaction, row id)` position, and read only below PostgreSQL's snapshot horizon
- a transaction committed late therefore cannot be skipped by an identity-only cursor; an old cursor replays stable event IDs
- module and HTTP forms share `RunBrokerConformance` and `PostDeliveryBrokerConformance` with the consumer fakes

`AddHarvestClient` registers the registry. `Client.Runs.AddHarvestRunClient` and
`Client.Delivery.AddHarvestDataClient` explicitly add the other HTTP brokers under its configured endpoint and key.
The module's `Module.*` registration extensions likewise opt each capability in separately.

---

## Wire

- Enums travel as camelCase labels (`ownFetch`, `pendingApproval`); durations as ISO 8601 (`PT1H`); nulls are omitted.
- Dictionary keys are camelCased on write (`JsonOptionsConstants.Default`): step keys are lowercase by rule, but any
  consumer-supplied dictionary (a subscription's `context`) loses a leading capital.
