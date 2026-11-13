# wow-two-sdk-beta.harvest — Contract

*Last updated: 2026-11-12*

> Everything in `packages/Abstractions`: the registry broker every form implements, and the seams the engine lanes and
> source readers build against. Interfaces and models only — no DI, options or I/O. Rules:
> wow-two-ws/conventions/development/capability-forms.md § Contract. The source host's own routes: § Source host.

---

## Registration

`IRegistrationBroker` (`Registration`) — tenants, their API keys, containers, sources and their groups, and pipelines.

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
| `UpdateSourceAsync(id, SourceUpdate)` | `SourceModel` with its display `name` and `groupId`; an omitted value keeps what is stored, a blank name clears it, `clearGroup` ungroups | `NotFound` source, group · `Validation` name over 200, a group of another container, a group with `clearGroup` |
| `DeclareSourceGroupAsync(SourceGroupDeclaration)` | `SourceGroupModel`; a name the container holds in any case returns that group unchanged | `NotFound` container · `Validation` blank or long name, description over 1000 |
| `ListSourceGroupsAsync(containerId)` | the container's groups, by name | `NotFound` |
| `DeclarePipelineAsync(PipelineDeclaration)` | `PipelineModel` with its schedule rows; `active` over an open or granted source, else `pendingApproval` | `NotFound` container, source · `Validation` source of another container, denied source, filtered source without scope, schedules, step overrides, limits · `Conflict` same `(container, source, scope key)` |
| `GetPipelineAsync(id)` · `ListPipelinesAsync(containerId)` | one · oldest first | `NotFound` |
| `UpdatePipelineAsync(id, PipelineUpdate)` | replaces name, full scope and schedules; sets each limit `Limits` states and keeps the rest; retains IDs/history, lifecycle and queued plans; omitted kinds disabled | `NotFound` pipeline · `Validation` as declaration, `maxPages` outside 1–50 · `Conflict` duplicate scope |

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
| `PUT api/pipelines/{id}` | `pipelines:manage` | edits its own private pipelines; the pool is 403 and foreign pipelines are 404 |
| `GET api/pipelines` | `pipelines:read` | the pool and its own tenant's; another tenant's is 404 |
| `PUT api/pipelines/{id}` with `limits` | `pipelines:manage`, operator | nothing (403); the same edit without `limits` follows the row above |
| `POST api/sources/groups` · `PUT api/sources/{id}` · `PUT api/sources/{id}/reading` | `harvest:manage-sources`: `sources:manage` held by the operator | nothing (403), a browser session's `sources:manage` included |
| `GET api/sources/overview` · `GET api/sources/groups` | `pipelines:read` (no `sources:read` exists) | the pool and its own tenant's; another tenant's is 404 |
| `GET api/sources/{id}/posts` · `GET api/posts/{id}/revisions` | `posts:read` | sources and posts of the pool and its own tenant's; another tenant's is 404 |

- Keys: Identity.Auth `Identity/ApiKeys` (`ApiKey` scheme, no local pass); `/api/system/status` stays open.
- The operator key (`Harvest:Operator:ApiKey`, `HarvestModuleOptions.OperatorApiKey`) holds every scope and no tenant;
  it comes from a host file or the vault. Tenant keys hold `HarvestScopeConstants.TenantScopes` only.
- Optional browser sessions use Identity.Client's BFF and an explicit organization-to-tenant binding. Granted
  `harvest:{scope}` permissions become Harvest scopes, excluding `tenants:manage` even for organization owners.
- Bound sessions can read their own tenant metadata and create their own private containers/sources when granted
  `containers:manage` / `sources:manage`. They cannot issue keys, create tenants, write the shared pool or read a
  foreign tenant. Existing tenant API-key restrictions in the table remain unchanged.
- Cookie writes require the session-bound `X-Csrf-Token`. An explicit service credential selects API-key auth even
  when invalid; a cookie cannot rescue an invalid key. API calls return 401/403 rather than an HTML sign-in redirect.
- The anonymous `/api/console/access` probe describes mode and authorized visibility without returning credentials.

---

## Engine seams

The lanes of v0.1 implement these in `packages/Module/{Lane}/`; the named source readers under `sources/` implement
the step contract. Every type references the BCL and the kernel only.

| Seam | Namespace | Implemented by | Called by |
|---|---|---|---|
| `ISourceAdapter` → `AdapterDescriptor` (`StepDescriptor`, plans per `WorkKind`) | `Steps` | Readers lane (generic family adapters) · `sources/*` (named sources) | Engine |
| `ISourceConfigurationProvider` → `SourceConfigurationModel` | `Sources` | Optional adapter metadata: scope schema and local readiness | Authorized source-capabilities API and shared UI |
| `IStepBroker.ExecuteAsync(StepItem, IStepContext)` → `StepResult` | `Steps` | the same adapters, one broker per step | Engine |
| `IStepContext` (`ResolvedPlan`, `Observe(StepSignal)`) | `Steps` | Engine lane (iteration 3) | step brokers |
| `StepRequirements` (`RenderNeed`, `MinDelay`) | `Steps` | vocabulary | adapters, Engine |
| outcomes: `FetchOutcome`, `ItemOutcome`, `LinkSignal` · `LinkObservation`, `RunReason` | `Steps` · `Runs` | vocabulary | adapters, Engine, Posts |
| `IFetchClientFactory` — direct clients, one user agent, a run's cookie jar | `Capabilities.Fetching` | Fetching lane (iteration 2), inline in `Module/Capabilities/` over core `Http/Safety` | Engine |
| `IFetchTransportFactory` → `IFetchTransportLease` | `Capabilities.Fetching` | direct default; explicit route/session providers in `sources/Integrations` | fetching policy handler, after permission/pause gates |
| `IChallengeDetector` (`ChallengeProbe` → `ChallengeVerdict`: challenge, block or rate limit; `ChallengeKind`) | `Capabilities.Challenges` | Fetching lane (generic) · adapters (their own) | step brokers |
| `IBrowserRenderer` | `Capabilities.Rendering` | declared, unbound in v0.1; a step that needs it reports `capabilityUnavailable` | Engine |
| `ICaptureReader` → `CaptureReadResult` (`Capture`, `DiscoveredUrl`) over `FetchedDocument` | `Readers` | Readers lane (iteration 4) | step brokers |
| `IPostExtractor` → `PostEnvelope` (`wow2.post.v1`) | `Extraction` | Extraction lane (iteration 5) | Engine |
| `PostState` · `LinkState` · `LinkRole` | `Posts` | Posts lane (iteration 6) over its tables | Delivery, console |
| `SubscriptionModel` · `DeliveryEvent` · `DeliveryEventTypes` · `CursorPage<T>` | `Delivery` | Delivery lane (iteration 7), which declares the subscription broker | consumers |

- A challenge, a block or a rate limit is a `FetchOutcome` the run records, and the host backs off. The generic engine
  never automatically retries through another route or submits a challenge. The source host may explicitly configure
  proxy/IP and supplied account-session leases, plus a source-aware challenge client. Provider failure never silently
  falls back to direct HTTP. Route secrets do not enter captures or delivery.
- A lease owns its client and account/IP reservation until disposal, including robots and allowed redirects. Leased
  sessions do not share cached bodies. The direct factory remains the default.
- The renderer serves pages that show their content only through JavaScript, never a challenge.
- The schema in the SQL migrations is the shared seam between lanes: `001-baseline`, `002-drop-escalation` (no
  ladder), `003-fetching`, `004-posts-delivery`, `005-subscription-filter`, `006-source-groups` (source groups, display
  names, the post revision log). Each lane maps the tables its folder's lead doc names.
- `Capture.ContentProjection` drives the content hash; a capture row exists only when it changes.
- Source readers can retain their versioned native field envelope in page metadata's `sourceFields`; extraction
  preserves it under `PostEnvelope.Extras["source"]`. Original publisher labels use `SourceTags`, independently
  of the shared-vocabulary `Tags`. Source snapshots and mapped values are inspectable without executing native HTML.
  `PostLinkModel.SourceFields` exposes each link's latest native snapshot independently of merged mapped fields.
- `IFetchDestinationPolicy` optionally restricts a named source's initial and redirected page URLs before transport.
  Robots, host/network restrictions and refusal backoff still apply independently.
- `ILinkAvailabilityReader` optionally validates a known link's identity and source-specific unavailable markers.
  An inconclusive HTTP 200 becomes `Unrecognized` with no existence signal; ordinary HTTP-only sources keep their
  existing check behavior. Direct 404/410 and conditional 304 retain their transport evidence.
- Explicit schema.org Product nodes map to `PostKind.Listing`, listed prices, seller and place without event dates
  or ticket semantics; the common rule version is `rules@4`.

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

- `IRunBroker`: `StartAsync`, `GetAsync`, `ListAsync`, `CancelAsync`, `SetPausedAsync`, `HostsAsync`,
  `SetReadingPaceAsync(sourceId, minDelayMs)` (1000–60000 ms on the source's host, returns its `SourceHealthModel`).
  Routes: `api/runs`, `api/runs/{id}/cancel`, `api/pipelines/{id}/pause`, `api/hosts?containerId`,
  `PUT api/sources/{id}/reading`.
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
- post links expose the last recorded availability observation and last confirmed-present time separately from
  their immutable content-capture time; outcome, HTTP status, run ID/kind and observation count explain the evidence
- next-check eligibility does not promise execution; the console separately reports enabled check schedules
- re-extracting a historical capture does not advance its last-seen time to the ingestion time
- merges retain an undo record; undo restores moved links and prevents the separated posts from being auto-merged again
- delivery stores immutable revision snapshots; `wow2.event.v1` selects the event projection while retaining post identity and provenance
- pull cursors contain the subscription and `(transaction, row id)` position, and read only below PostgreSQL's snapshot horizon
- a transaction committed late therefore cannot be skipped by an identity-only cursor; an old cursor replays stable event IDs
- module and HTTP forms share `RunBrokerConformance` and `PostDeliveryBrokerConformance` with the consumer fakes

`AddHarvestClient` registers the registry. `Client.Runs.AddHarvestRunClient`, `Client.Delivery.AddHarvestDataClient`
and `Client.Sources.AddHarvestSourceClient` explicitly add the other HTTP brokers under its configured endpoint and key.
The module's `Module.*` registration extensions likewise opt each capability in separately.

---

## Sources overview

`ISourceOverviewBroker` (`Sources`) — the console's Sources area, read-only over the registry, run, post and delivery
stores. Groups and display names are `IRegistrationBroker`'s; the reading pace is `IRunBroker`'s.

| Member | Returns | Failures |
|---|---|---|
| `ListOverviewAsync(containerId)` | one `SourceOverviewModel` per registered source, by name: `state`, `lastHarvest`, `nextHarvestAt`, `newPostsDaily` (14 UTC days, today last), `sections` (one per pipeline), `health` | `NotFound` container |
| `ListSourcePostsAsync(sourceId, limit = 200)` | `SourcePostModel` newest first: title, kind, state, revision, first seen, last changed, the section that first captured it, stated start, URL, the first image's source URL (never a `fileRef`) | `NotFound` source · `Validation` limit outside 1–500 |
| `ListPostRevisionsAsync(containerId, postId)` | `PostRevisionModel` newest first: `revision`, `at`, `changed`; empty for a post from before `006` | `NotFound` post in the container |

- `state`: `harvesting` while a run is queued or running; else `needsAttention` when the host is paused, has failed 3
  or more times in a row, or the last harvest failed; else `healthy` when a section harvests; else `idle`.
- A section harvests when its pipeline is active and has an enabled capture schedule. Rules in full:
  `packages/Module/Sources/sources.md`.
- The post resolve writes `harvest_post_revisions` on create (`changed = {created}`) and on each revision rise, naming
  the top-level `base` fields that changed (never `extraction`, `fieldSources`, `flags`).
- Routes: `GET api/sources/overview?containerId`, `GET api/sources/{id}/posts?limit`,
  `GET api/posts/{id}/revisions` (`containerId` optional; without it the caller's visible containers are searched),
  and the registry's `GET`/`POST api/sources/groups`, `PUT api/sources/{id}`.

| Form | Implementation | Registration |
|---|---|---|
| 1 | `ModuleSourceOverviewBroker` (Module, `Sources/`) | `AddHarvestSources()` |
| 2/3 | `ApiSourceOverviewBroker` (Client) | `AddHarvestSourceClient()` after `AddHarvestClient` |
| fake | `FakeSourceOverviewBroker` (Testing) over the registry, run, post and delivery fakes | constructed |

- Conformance: `SourceOverviewBrokerConformance` runs against all three; groups, names and `maxPages` join
  `RegistrationBrokerConformance`, the reading pace `RunBrokerConformance`.

---

## Source host

`HarvestSources.Api` serves every route above through Hosting, with the same policies and `{ data }` envelopes, plus
operator setup (`HarvestSources:Audience` `internal` only):

| Method | Route | Policy | Result |
|---|---|---|---|
| GET | `/api/ocharo/sources/presets` | operator `tenants:manage` | original preset keys, names, seeds and authority |
| GET | `/api/ocharo/sources/registry` | operator `tenants:manage` | `{ format, reviewedOn, groups }`; a read neither provisions nor fetches |
| POST | `/api/ocharo/sources/setup` | operator `tenants:manage` | `{ tenantId, containerId, sources: [{ sourceKey, seed, groupId, channelId, eventFamily?, sourceId, pipelineId, subscriptionId }] }` |

- The registry (`sources/Module/Ocharo/ocharo-event-sources.json`, [review](../research/ocharo-source-registry.md))
  lists each publisher, its channels and each channel's collection mode: `harvest` (installed reader), `provider`
  (installed opt-in integration; credentials and publisher access required), `planned`, `manual`, `reference`. Rank
  measures relevance, not readiness; only owned channels with an installed collector can be provisioned.
- Setup takes `{ enableSchedules: false, includeInstagram: false, groupIds?: string[] }`. Without `groupIds` it keeps
  the original five website scopes and, when asked, the three Instagram routes; explicit IDs select every eligible
  channel in those groups. An unknown group, or a group with no eligible channel, fails before any row is written.
  CAEx is selectable explicitly and has no link-check schedule. The two Teplo event channels share one Instagram
  source with distinct pipelines, subscriptions and event families.
- Setup creates missing identities and keeps existing rows, cadences and keys. New website captures run every 6 hours
  when enabled, plus a daily link check for individual-link sources; Instagram schedules start disabled. Sources stay
  private to the tenant, non-pool-shareable, AI-disabled, robots-obeying and window-complete; differing existing
  AI/sharing/robots settings need operator review. The host verb `ocharo setup|status`:
  `engineering/deployment/deployment.md` § Provisioning Ocharo on a target.
- Subscriptions request `wow2.event.v1` with filter `ocharo-fashion-v1` and context `catalog=ocharo-fashion`,
  `sourceGroup`, `sourceChannel` and optional `eventFamily`. Existing subscriptions keep their IDs and context; the
  setup response maps them to groups. Each observed event keeps its own source link; groups never auto-merge editions.
- The event payload keeps canonical source provenance in `links` and registration/ticket/other URLs in
  `contentLinks`. Dates keep their precision and evidence; application deadlines, closed calls and participation
  evidence stay in the source's `schemaOrg` namespace. Missing eligibility, fees or routes are unknown. Afisha/ANONS
  articles without an event announcement stay raw posts outside this stream.
- The host writes nothing to Events or Desk and promotes no candidate; Events pulls and classifies (events-integration #53).
- `ICaptchaSolver` and the IP broker JSON protocol: [integration-providers.md](../research/integration-providers.md).
  No provider credential reaches registry rows, captures or delivery.

### Marketplace sources

- `AddHarvestListingSources` composes the `olx-uz` adapter, the `marketplace-listing-v1` delivery filter (listings
  only) and `MarketplaceProvisioner`; registration creates no rows. Listing posts skip the fashion-event filter.
- A marketplace profile (`MarketplaceProfiles`) names a consumer's tenant, private container, saved searches, cadence,
  run budget and key name. Setup applies it idempotently, like Ocharo's, through the `marketplace setup|status` verb
  and operator routes (`internal` only):

| Method | Route | Policy | Result |
|---|---|---|---|
| GET | `/api/marketplace/profiles` | operator `tenants:manage` | the reviewed profiles; a read neither provisions nor fetches |
| POST | `/api/marketplace/{profile}/setup` | operator `tenants:manage` | `{ profile, tenantId, containerId, sourceId, searches: [{ key, url, pipelineId, subscriptionId }], consumerKey? }` |
| GET | `/api/marketplace/{profile}/status` | operator `tenants:manage` | each search's pipeline with its schedules, its subscription and the active key count |

- Setup takes `{ enableSchedules: false, issueKey: false, rotateKey: false }`. One pipeline and one `wow2.post.v1`
  subscription per search, context `catalog` and `search`. The consumer's tenant key holds `events:read` only;
  `consumerKey.secret` is present only in the response that issued it. Haven's profile and target steps:
  `engineering/deployment/deployment.md` § Provisioning Haven on a target.
- OLX implements `IFetchDestinationPolicy` and `ILinkAvailabilityReader`: initial and redirected requests stay on
  supported public routes; known-link checks validate the ad ID, explicit unavailable markers and matching native
  status. An unknown layout, challenge or unrelated redirect stays inconclusive; search windows never establish
  disappearance. [OLX review](../research/olx-source-review.md)

---

## Wire

- Enums travel as camelCase labels (`ownFetch`, `pendingApproval`); durations as ISO 8601 (`PT1H`); nulls are omitted.
- Dictionary keys are camelCased on write (`JsonOptionsConstants.Default`): step keys are lowercase by rule, but any
  consumer-supplied dictionary (a subscription's `context`) loses a leading capital.
