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
- Step overrides and capability policies name rungs from `CapabilityRungs` (`R0`–`R8`); a ladder climbs strictly,
  inside its floor and ceiling; step keys and lanes match `StepConstants.KeyPattern`.

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
| `StepRequirements` (floor · ladder · ceiling, `EgressNeed`, `SessionNeed`, `RenderNeed`, `ChallengePolicy`), `CapabilityRungs` | `Steps` · `Capabilities` | vocabulary | adapters, the resolver |
| outcomes: `FetchOutcome`, `ItemOutcome`, `LinkSignal` · `LinkObservation`, `RunReason` | `Steps` · `Runs` | vocabulary | adapters, Engine, Posts |
| `IEgressPool` · `IEgressClientFactory` (`EgressRequest`, `EgressLease`) | `Capabilities.Egress` | Fetching lane (iteration 2), inline in `Module/Capabilities/`; moves to core `Http/Egress` by namespace | the resolver |
| `ISessionLeases` (`SessionRequest`, `SessionLease`) | `Capabilities.Sessions` | Fetching lane; Social-shaped | the resolver |
| `IProfileCatalog` (`RequestProfile`) | `Capabilities.Profiles` | Fetching lane | the resolver |
| `IChallengeDetector` (`ChallengeProbe` → `ChallengeVerdict`, `ChallengeKind`) | `Capabilities.Challenges` | Fetching lane (generic) · adapters (their own) | step brokers |
| `IBrowserRenderer` · `IChallengeSolver` | `Capabilities.Rendering` · `.Challenges` | declared, unbound in v0.1; only `R0`–`R1` are live, later rungs report `capabilityUnavailable` | the resolver |
| `ICaptureReader` → `CaptureReadResult` (`Capture`, `DiscoveredUrl`) over `FetchedDocument` | `Readers` | Readers lane (iteration 4) | step brokers |
| `IPostExtractor` → `PostEnvelope` (`wow2.post.v1`) | `Extraction` | Extraction lane (iteration 5) | Engine |
| `PostState` · `LinkState` · `LinkRole` | `Posts` | Posts lane (iteration 6) over its tables | Delivery, console |
| `SubscriptionModel` · `DeliveryEvent` · `DeliveryEventTypes` · `CursorPage<T>` | `Delivery` | Delivery lane (iteration 7), which declares the subscription broker | consumers |

- The schema in SQL `001` is the shared seam between lanes: each lane maps the tables its folder's lead doc names.
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

## Wire

- Enums travel as camelCase labels (`ownFetch`, `pendingApproval`); durations as ISO 8601 (`PT1H`); nulls are omitted.
- Dictionary keys are camelCased on write (`JsonOptionsConstants.Default`): step keys are lowercase by rule, but any
  consumer-supplied dictionary (a subscription's `context`) loses a leading capital.
