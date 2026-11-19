# wow-two-sdk-beta.harvest — Contract

*Last updated: 2026-11-19*

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
| `UpdateSourceAsync(id, SourceUpdate)` | `SourceModel` with its display `name`, `groupId`, optional master `paused` state and `modelProcessing` mode (`off` · `manual` · `auto`; the older `aiAllowed` sets `auto` or `off`); an omitted value keeps what is stored, a blank name clears it, `clearGroup` ungroups | `NotFound` source, group · `Validation` name over 200, a group of another container, a group with `clearGroup`, `aiAllowed` disagreeing with `modelProcessing` · `Conflict` master-pause change on an unavailable or retired source |
| `DeclareSourceGroupAsync(SourceGroupDeclaration)` | `SourceGroupModel`; a name the container holds in any case returns that group unchanged | `NotFound` container · `Validation` blank or long name, description over 1000 |
| `ListSourceGroupsAsync(containerId)` | the container's groups, by name | `NotFound` |
| `DeclarePipelineAsync(PipelineDeclaration)` | `PipelineModel` with its schedule rows; `active` over an open or granted source, else `pendingApproval` | `NotFound` container, source · `Validation` source of another container, denied source, filtered source without scope, schedules, step overrides, limits · `Conflict` same `(container, source, scope key)` |
| `GetPipelineAsync(id)` · `ListPipelinesAsync(containerId)` | one · oldest first | `NotFound` |
| `UpdatePipelineAsync(id, PipelineUpdate)` | replaces name, full scope and schedules; sets each limit `Limits` states and keeps the rest; retains IDs/history, lifecycle and queued plans; omitted kinds disabled | `NotFound` pipeline · `Validation` as declaration, `maxPages` outside 1–50 · `Conflict` duplicate scope |

- Normalization: web handles reduce to their IDNA host; Telegram handles become `tg:{username}`; the scope key is `*`
  for the whole source, else the normalized URL (sorted query, no fragment) and the parameters sorted by name.
- Posture switches ship permissive (`permission` open, `modelProcessing` `auto`, `poolShareable`, `robotsPolicy` obey); a
  ban or a takedown is one row's update. `posture` is required and gates nothing.
- `modelProcessing` (`ModelProcessingMode`, migration `016`): `off` sends nothing to a model; `manual` queues nothing on
  its own, but a re-run naming posts or listing sources, an eval item or a Process stage started by hand may; `auto` also queues on
  every write and lets scheduled Process stages run. `SourceModel.aiAllowed` stays on the wire, true unless the mode is
  `off`; on a registration or an update `aiAllowed` `true`/`false` maps to `auto`/`off`, and `modelProcessing` wins.
  A post takes the strictest mode of the sources supplying its title and text (`../development/ai-operations.md`).
- `paused: true` persists `SourceState.Paused`; `false` resumes an active/paused source without changing permission,
  individual pipeline states or schedule rows. The source update shares the scheduler transaction lock. Pausing
  prevents new manual/scheduled runs and claims of queued work; an already executing request may finish. Display-name
  and group changes with `paused` omitted preserve the source state. The existing source-management route and access
  policy apply; this is not a pipeline bulk-enable operation.
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
| `PUT api/pipelines/{id}` · `PUT api/pipelines/{id}/stages` | `pipelines:manage` | edits its own private pipelines and their stages; the pool is 403 (404 on `/stages`) and foreign pipelines are 404 |
| `GET api/pipelines` | `pipelines:read` | the pool and its own tenant's; another tenant's is 404 |
| `PUT api/pipelines/{id}` with `limits` | `pipelines:manage`, operator | nothing (403); the same edit without `limits` follows the row above |
| `POST api/sources/groups` · `PUT api/sources/{id}` · `PUT api/sources/{id}/reading` | `harvest:manage-sources`: `sources:manage` held by the operator | nothing (403), a browser session's `sources:manage` included |
| `GET api/sources/overview` · `GET api/sources/groups` | `pipelines:read` (no `sources:read` exists) | the pool and its own tenant's; another tenant's is 404 |
| `GET api/sources/{id}/posts` · `GET api/sources/{id}/processed` · `GET api/posts/{id}/revisions` | `posts:read` | sources and posts of the pool and its own tenant's; another tenant's is 404 |
| `GET api/entities` · `api/entities/{id}` · `api/entities/{id}/posts` · `api/entities/calendar` · `api/entities/events` | `posts:read` (no `entities:read` exists) | entities of the pool and its own tenant's containers; another tenant's container, or an entity of another container, is 404 |

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
  names, the post revision log), through `011-taxonomy` (the IAB 3.1 category tree), `012-post-tags` (tagging answers
  and the post work queue), `013-post-extractions` (Layer 2 answers and their queue kinds), `014-entities` (entities,
  occurrences, relations, keep-apart pairs, unsure flags and the `entity-resolve` queue kind) and `015-entity-events`
  (the entity change feed). Each lane maps the tables its folder's lead doc names.
- `Capture.ContentProjection` drives the content hash; a capture row exists only when it changes.
- Source readers can retain their versioned native field envelope in page metadata's `sourceFields`; extraction
  preserves it under `PostEnvelope.Extras["source"]`. Original publisher labels use `SourceTags`, independently
  of the shared-vocabulary `Tags`. Source snapshots and mapped values are inspectable without executing native HTML.
- `Tags` also carries the Layer 1 `post-tags` answer, tier `SmallModel`, beside any mapped tags: `type:{entityType}`
  (`event` · `listing` · `none`), `domain:{node}` for each IAB 3.1 node and its ancestors (`domain:style-fashion`,
  `domain:style-fashion/womens-fashion`), `topic:{slug}` (open English topics), version `post-tags@{promptVersion}`,
  score = the answer's confidence; `Extras["postTags"].titleEn` holds the English title. The taxonomy is IAB Tech Lab
  Content Taxonomy 3.1 (CC BY 3.0) minus `Events`, `Genres` and `Sensitive Topics`; `development/post-tags.md`.
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
- a post whose title or text has no done `post-tags` answer is queued in the write's transaction when tagging is on and
  its title and text come from sources that allow AI; a done answer that changes `base` raises the revision and appends
  `post.changed`, and that tag-only revision keeps its content hash, so it queues nothing
- a done `post-tags` answer about the post's current title and text queues Layer 2 extraction in the same transaction
  when `IPostExtractionDemand` wants its (entity type, domain) pair; the answer is stored per post, content and use case
  in `harvest_post_extractions` with verbatim evidence for every date, price and rental term, and is not part of the
  post or its delivery events until the entity layer reads it; `development/extraction.md`
- a done extraction answer queues the post's entity resolution in the same transaction when `Harvest:Entities:Enabled`
  is set; resolution binds the post with a role (`announcement`, `update`, `report`, `mention`) to one event or listing
  in `harvest_entity_relations`, keeps edition history in `harvest_entity_occurrences`, flags a weak or ambiguous match
  instead of binding it, and never writes the post or a delivery event; § Entities reads them;
  `development/entities.md`
- delivery stores immutable revision snapshots; `wow2.event.v1` selects the event projection while retaining post identity and provenance
- a post the subscription holds whose newest revision fails its filter or section arrives once as `post.unmatched`
  (that revision's identity, number and payload); later failing revisions, a restore from gone included, are withheld,
  lifecycle events (`link.state`, `post.merged`, `post.unmerged`, `post.purged`) still arrive, and the next passing
  revision arrives as `post.changed`. An absent or throwing filter decides nothing and sends no `post.unmatched`
- pull cursors contain the subscription and `(transaction, row id)` position, and read only below PostgreSQL's snapshot horizon
- a transaction committed late therefore cannot be skipped by an identity-only cursor; an old cursor replays stable event IDs
- module and HTTP forms share `RunBrokerConformance` and `PostDeliveryBrokerConformance` with the consumer fakes

`AddHarvestClient` registers the registry. `Client.Runs.AddHarvestRunClient`, `Client.Delivery.AddHarvestDataClient`
and `Client.Sources.AddHarvestSourceClient` explicitly add the other HTTP brokers under its configured endpoint and key.
The module's `Module.*` registration extensions likewise opt each capability in separately.

---

## Entities

`IEntitiesBroker` (`Entities`) — the entities posts are about and their change feed, read-only, inside one container
(build plan step D, scfd-entity-model #2 and #8). Resolution, merge and split are the module's `IEntityBroker` and the
`entities` host verb.

| Member | Returns | Failures |
|---|---|---|
| `SearchAsync(containerId, query?, entityType?, domain?, limit = 20)` | `EntitySummaryModel` best first (newest first without a query): type, name, aliases, primary domain, status, cadence, `score`, `years` (each `EntityYearSummaryModel`: real occurrences, posts, `predicted`), the predicted next start, counts of occurrences, posts and sources | `NotFound` container · `Validation` query over 300, type, domain key, limit outside 1–100 |
| `GetAsync(containerId, entityId)` | `EntityTimelineModel`: the entity, `requestedId` when merged, posts and sources, `years` each with its `OccurrenceModel`s (label, starts, ends, precision, certainty, `predicted`, status, `place`) and posts | `NotFound` container or an entity the container does not hold |
| `ListPostsAsync(containerId, entityId, year?, role?, limit = 50, cursor?)` | `CursorPage<EntityPostModel>` newest posted first: post id, role, changes, occurrence, `year`, title, link, source id and name, posted at, first seen, bound at | as `GetAsync` · `Validation` year outside 1900–2200, role, limit outside 1–200, a cursor of another entity |
| `CalendarAsync(containerId, from, to, domain?, entityType?)` | `EntityCalendarEntryModel` earliest first: the event (id, type, name, domain, cadence), its `OccurrenceModel` and posts | `NotFound` container · `Validation` `to` before `from`, a window over 366 days, type, domain |
| `PullEventsAsync(containerId, cursor?, limit = 100, entityType?, domain?)` | `CursorPage<EntityEvent>`: event id, `EntityEventTypes` type, when, the entity's id, type, name, domain, status and cadence, the occurrence after and `previous` before, `changes`, `relatedEntityId`, `replacesOccurrenceId`, the post that caused it | `NotFound` container · `Validation` limit outside 1–500, a malformed cursor or one of another container |

- A post's `year` is its occurrence's start year, else its posted year, else its first-seen year; only surviving posts
  count. `EntityQueryRules` holds the request rules every form applies; `EntityErrors` the entity errors.
- The change feed is `harvest_entity_events`, appended in the changing transaction: `entity.created`, `entity.changed`,
  `entity.merged`, `entity.split`, `occurrence.added`, `occurrence.changed`, `occurrence.confirmed`,
  `occurrence.predicted`, `occurrence.removed`. Its cursor follows the pull rules: `(transaction, row id)` below the
  snapshot horizon, opaque and bound to its container; an older cursor replays, an empty page returns the cursor given.
- Subscription delivery does not read the feed; per-subscription routing of entity events is the selector's (S09).
- Routes: `GET api/entities?containerId&q&type&domain&limit`, `GET api/entities/{id}?containerId`,
  `GET api/entities/{id}/posts?containerId&year&role&limit&cursor`, `GET api/entities/calendar?containerId&from&to&domain&type`,
  `GET api/entities/events?containerId&cursor&limit&type&domain` (`EntityRouteConstants`); `{ data }` envelope.

| Form | Implementation | Registration |
|---|---|---|
| 1 | `ModuleEntitiesBroker` (Module, `Entities/`) | `AddHarvestEntityReads()`, which `AddHarvestEntities()` and Hosting call |
| 2/3 | `ApiEntitiesBroker` (Client) | `AddHarvestEntityClient()` after `AddHarvestClient`; the caller needs `posts:read` |
| fake | `FakeEntitiesBroker` (Testing) over the registry and post fakes; `Seed` stands in for resolution, `Append` adds a change | constructed |
| fe | `createEntitiesClient` (`fe-harvest/kit`, also `createHarvestClient().entities`) over the same routes; the models in `fe-harvest/contracts` (`EntityModels.ts`) | the console's Entities page |

- Conformance: `EntitiesBrokerConformance` runs against all three; it seeds through `SeedAsync` (the module's writers
  for the module and client, `Seed` for the fake), so the client form always targets the in-process host.

---

## Sources overview

`ISourceOverviewBroker` (`Sources`) — the console's Sources area, read-only over the registry, run, post and delivery
stores. Groups and display names are `IRegistrationBroker`'s; the reading pace is `IRunBroker`'s.

| Member | Returns | Failures |
|---|---|---|
| `ListOverviewAsync(containerId)` | one `SourceOverviewModel` per registered source, by name: operational `state`, persisted `sourceState`, `permission`, `lastHarvest` with its work `kind`, `nextHarvestAt`, `newPostsDaily` (14 UTC days, today last), `sections` (one per pipeline), `health` | `NotFound` container |
| `ListSourcePostsAsync(sourceId, limit = 200)` | `SourcePostModel` newest first: title, kind, state, revision, first seen, last changed, the section that first captured it, stated start, URL, the first image's source URL (never a `fileRef`) | `NotFound` source · `Validation` limit outside 1–500 |
| `QuerySourcePostsAsync(sourceId, query)` | `SourcePostPage` with matching items, an optional continuation cursor, counts and facets over the full query | `NotFound` source · `Validation` unsupported filters, limits or cursor |
| `QuerySourceProcessedAsync(sourceId, query)` | `SourceProcessedPage` newest first seen first: every surviving post with a `post-tags` answer — its `ProcessedTagsModel` (entity type, domains with labels, topics, about, timeframe, English title, flags), its `classification`, the extraction of its tagged type about the same content with an event or listing summary — each answer's `ProcessedAnswerModel` (provider, model, prompt version, cost and latency from the inference log); `nextCursor`, `total` | `NotFound` source · `Validation` entity type, limit outside 1–90, a cursor of another source or filters (`SourceProcessedQueryRules`) |
| `ListPostRevisionsAsync(containerId, postId)` | `PostRevisionModel` newest first: `revision`, `at`, `changed`; empty for a post from before `006` | `NotFound` post in the container |

- For an active source with open/granted permission, operational `state` is `harvesting` while a run is queued or
  running; else `needsAttention` when the host is paused, has failed 3 or more times in a row, or the last collection
  or processing run failed; else `healthy` when a section has automatic work; else `idle`. A master-paused source is
  `idle`; another unreadable source needs attention. `sourceState` and `permission` distinguish these causes without
  inferring enablement from operational health.
- A section's `harvesting` requires a readable source, an active pipeline and an enabled, non-manual Capture,
  Discover, Harvest or Process schedule whose stage is not disabled. `nextDueAt` and `nextWorkKind` identify the
  earliest such due schedule. `lastHarvest.kind` identifies the latest source collection/processing run.
- `sections[].harvestKind` selects the available manual action: enabled legacy Capture, then a configured,
  non-disabled Harvest, Discover or Process stage. Stage automation may be off while manual execution remains
  available. An unreadable source or inactive pipeline has no manual kind. Nullable kind fields are omitted on the
  wire. Older servers lacking `sourceState` cannot support the console's master source control; the client withholds
  those writes instead of treating an ignored `paused` field as a successful update.
- The post resolve writes `harvest_post_revisions` on create (`changed = {created}`) and on each revision rise, naming
  the top-level `base` fields that changed (never `extraction`, `fieldSources`, `flags`).
- The processed read picks the tags answer a post's own resolve picks (the done answer about its current title and
  text, else the newest done one, else the newest attempt) and the extraction about that answer's content; cost and
  latency are the answer's attempt as the AI module's inference log holds it, read by the post revision it answered,
  and absent on a host without the log; the fake keeps no log. It carries no post text beyond the title, and its
  summaries leave out the evidence quotes.
- Routes: `GET api/sources/overview?containerId`, `GET api/sources/{id}/posts?limit`,
  `GET api/sources/{id}/processed?entityType&verdict&extracted&limit&cursor`,
  `GET api/posts/{id}/revisions` (`containerId` optional; without it the caller's visible containers are searched),
  and the registry's `GET`/`POST api/sources/groups`, `PUT api/sources/{id}`.

| Form | Implementation | Registration |
|---|---|---|
| 1 | `ModuleSourceOverviewBroker` (Module, `Sources/`) | `AddHarvestSources()` |
| 2/3 | `ApiSourceOverviewBroker` (Client) | `AddHarvestSourceClient()` after `AddHarvestClient` |
| fake | `FakeSourceOverviewBroker` (Testing) over the registry, run, post and delivery fakes | constructed |
| fe | `createHarvestClient` (`fe-harvest/kit`): `listSourceOverview`, `querySourcePosts`, `querySourceProcessed` | the console's Sources pages |

- Conformance: `SourceOverviewBrokerConformance` runs against all three, seeding answers through `SeedAnswersAsync`
  (`ProcessedAnswerSeed`: SQL rows for the module and client, `SeedAnswers` for the fake); groups, names and `maxPages` join
  `RegistrationBrokerConformance`, the reading pace `RunBrokerConformance`.

### Stages, discoveries and Posts queries

Every route checks container access and returns 404 for an item the caller cannot see. Successful payloads use
the standard `data` envelope.

| Route | Access | Request and response |
|---|---|---|
| `GET /api/pipelines/{id}/stages` | `pipelines:read` and container read | `PipelineStageModel[]` with effective options, schedule, catalog when available, operation keys, recent runs and, for Process, the registered processor keys with their console labels (`processorLabels`) |
| `PUT /api/pipelines/{id}/stages` | `pipelines:manage` and container write, as `PUT /api/pipelines/{id}` | A `ScheduleDeclaration` with stage options for Discover, Harvest or Process. Returns whether settings were saved |
| `GET /api/pipelines/{id}/discoveries` | `pipelines:read` and container read | Optional `state` (`pending`, `captured`, `failed`, `gone`), `offset` and `limit` (default 50, maximum 200). Returns `PipelineDiscoveryModel[]` |
| `GET /api/runs/{id}/audit` | `pipelines:read` and the run's container read | Optional `offset`, `limit` (default 100, maximum 200) and `newestFirst`. Returns `RunAttemptModel[]` with outcomes, retry times, HTTP statuses and recorded operations |
| `POST /api/sources/{id}/posts/query` | `posts:read` and container read | `SourcePostQuery` accepts title search, facet selections, optional post ID, cursor and limit (default 30, maximum 90). `SourcePostPage` returns items, `nextCursor`, `total`, `searchTotal`, counted facets and page size bounds |

Search, filter and count operations run before pagination. A cursor continues the same source, search and filter
selection. Facet counts apply the search and all selections except the facet being counted.

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
  the original five website scopes and, when asked, the registry's default Instagram routes; explicit IDs select every eligible
  channel in those groups. An unknown group, or a group with no eligible channel, fails before any row is written.
  CAEx is selectable explicitly and has no link-check schedule. Instagram accounts have separate publisher sources
  and pipelines. Teplo Fest and Teplo Store belong to the Teplo Store group; their event-family metadata remains distinct.
- Setup creates missing identities and keeps existing rows, cadences and keys. New website captures run every 6 hours
  when enabled, plus a daily link check for individual-link sources; Instagram schedules start disabled. Sources stay
  private to the tenant, non-pool-shareable, AI-disabled, robots-obeying and window-complete; differing existing
  AI/sharing/robots settings need operator review. The host verb `ocharo setup|status`:
  `engineering/deployment/deployment.md` § Provisioning Ocharo on a target.
- Subscriptions request `wow2.event.v1` with filter `ocharo-fashion-v1` and context `catalog=ocharo-fashion`,
  `sourceGroup`, `sourceChannel` and optional `eventFamily`. Existing subscriptions keep their IDs and context; the
  setup response maps them to groups. Each observed event keeps its own source link; groups never auto-merge editions.
- The event payload keeps canonical source provenance in `links` and registration/ticket/other URLs in
  `contentLinks`. Dates keep their precision and evidence. `starts` and `ends` are `{ value, precision, certainty }`;
  `certainty` is `stated` for a date the post states and `estimated` for one the extraction pass estimated from an
  undated post (S17, `../development/extraction.md` § Estimated dates; additive). A stated date always replaces an
  estimate. An application deadline a reader states travels in the
  top-level `deadlines` list (additive to `wow2.event.v1`; `[]` when none is stated):
  `{ kind: "application", value, precision, raw, status: open | closed | unknown }`, copied from
  `schemaOrg.craftfair.applicationDeadline` (status from its badge), `schemaOrg.uzexpocentre.applicationDeadline` and
  `schemaOrg.afisha.applicationDeadlineEvidence.date`. The namespaces stay as recorded, with participation evidence;
  missing eligibility, fees or routes are unknown. Afisha/ANONS articles without an event announcement stay raw posts
  outside this stream.
- `classification` `{ classifier, verdict, confidence, reason, kind }` and the top-level `kind` come from the
  `post-tags` pass when tagging is on and a wanted pair is set: the verdict for the pairs, `kind` the `extract-event`
  format or the entity type (S16, `../development/post-tags.md` § Classification); both stay null otherwise. Filter
  `ocharo-relevance-v1` passes what `ocharo-fashion-v1` passes plus a `relevant` verdict at 0.5 or above on an event
  tagged under `style-fashion`; no subscription uses it until the selector (S09).
- `titles` `[{ text, language: "en", machine: true }]` follows `title` (additive to `wow2.event.v1`): SCFD's English title
  for the post, from the `title_en` of the newest done `post-tags` answer (`extras.postTags.titleEn`, trimmed). It is
  absent, never `null` or `[]`, for a post with no answer or an answer without one, so such a post keeps its earlier
  bytes. `title` stays the source's verbatim text in the post's `language`; `machine: true` marks a model's translation,
  which a consumer shows for an `en` reader or as a fallback and never as the source title. It can repeat `title` for a
  post already in English. A revision carries the title of the answer in force when it was written: the revision that
  writes the tags carries it with `classification`, later captures keep it until a new answer replaces it, and a
  revision before any answer has none (golden `12-english-title.json`; `11-estimated.json` carries it from its tagged
  revisions on).
- The host writes nothing to Events or Desk and promotes no candidate; Events pulls and classifies (events-integration #53).
- `ICaptchaSolver` and the IP broker JSON protocol: [integration-providers.md](../research/integration-providers.md).
  No provider credential reaches registry rows, captures or delivery.

### AI operator controls

With shared AI on (`Harvest:Processing:Ai:Enabled`, `internal` only), the host serves the AI passes' operator controls,
each mirrored by a `processing` verb, and mounts the AI module's own API
([ai-operations](../development/ai-operations.md)):

| Method | Route | Policy | Result |
|---|---|---|---|
| POST | `/api/processing/reruns` | `harvest:manage-storage`: `containers:manage` held by the operator | `PostRerunModel`: `202` with `runId` and its status route, or `200` for `dryRun`; queued, skipped and refused counts and a cost estimate per use case |
| GET | `/api/processing/reruns/{runId}` | the same | `PostRerunStatusModel`: the counts it queued with and, per work kind, the rows still waiting, running or retrying |
| POST | `/api/processing/eval-sets` | the same | `PostEvalSetModel`: a seeded sample of stored posts added to an AI eval set |
| any | `/api/ai/*` (`MapAiApi`) | each route's own `ai.*` scope, which an operator holds; a live SCFD caller | the AI module's use cases, prompts, labels, prices, runs, inference log, eval sets, eval runs and reports |

- A tenant key reaches none of them (`403`); without shared AI, `api/ai` is not mapped (`404`) and the processing
  routes refuse (`409`).

### Marketplace sources

- `AddHarvestListingSources` composes the OLX, Domtut new-build and Joymee readers, the `marketplace-listing-v1` delivery filter
  and marketplace setup and imports. Listing posts skip the fashion-event filter.
- A marketplace profile (`MarketplaceProfiles`) names a consumer's tenant, private container, saved searches, cadence,
  run budget and key name. Setup applies it idempotently, like Ocharo's, through the `marketplace setup|status` verb
  and operator routes (`internal` only):

| Method | Route | Policy | Result |
|---|---|---|---|
| GET | `/api/marketplace/profiles` | operator `tenants:manage` | the reviewed profiles; a read neither provisions nor fetches |
| POST | `/api/marketplace/{profile}/setup` | operator `tenants:manage` | `{ profile, tenantId, containerId, searches: [{ key, url, sourceId, pipelineId, subscriptionId }], consumerKey? }` |
| GET | `/api/marketplace/{profile}/status` | operator `tenants:manage` | each search's pipeline with its schedules, its subscription and the active key count |
| POST | `/api/marketplace/{profile}/searches/{search}/listings` | operator `tenants:manage` | imports one authorized rental into its tenant's subscription |

- Setup takes `{ enableSchedules: false, issueKey: false, rotateKey: false, allowAi: false }`; `allowAi` opens the
  collected sources to model processors such as `rental-terms@1`, and a later setup without it keeps them open. One
  pipeline and one `wow2.post.v1` subscription per search, context `catalog` and `search`. The consumer's tenant key holds `events:read` only;
  `consumerKey.secret` is present only in the response that issued it. Haven's profile and target steps:
  `engineering/deployment/deployment.md` § Provisioning Haven on a target.
- OLX implements `IFetchDestinationPolicy` and `ILinkAvailabilityReader`: initial and redirected requests stay on
  supported public routes; known-link checks validate the ad ID, explicit unavailable markers and matching native
  status. An unknown layout, challenge or unrelated redirect stays inconclusive; search windows never establish
  disappearance. [OLX review](../research/olx-source-review.md)
- Domtut reads the server-rendered new-build catalog (`/catalog-nedvijimosty`) and complex pages, keyed by the page's
  numeric post ID; its `/filter/` searches, rent included, are JavaScript shells and are not fetched. A redirect to
  `/404`, which robots.txt disallows, stays inconclusive. `sources/Domtut/domtut.md`
- Haven's `birbir-collection` uses a separate site pipeline and private subscription with the BirBir collector.
  Its source capabilities declare `sourceFieldsSchema`, the JSON Schema for the `birbir.ad.v1` listing envelope.
  Setup registers the public site source with open permission; the BirBir collection profile uses
  the open source path. [BirBir reader](../codebase/harvest.backend-services/sources/BirBir/birbir.md)
- Haven's `birbir-apartments-rent` and `domtut-apartments-rent` searches are restricted to imports. Setup reports
  `importOnly: true` and creates no schedules for them. Import requests carry `externalId`, `url` and `title`;
  `description`, `priceText`, `city`, `district`, `sellerName`, `publishedAt`, `attributes` and `imageUrls` are optional.
  Use content supplied or licensed for the private Haven tenant. A repeat with identical content reuses its capture.
- `joymee-apartments-rent` reads the public Joymee JSON catalog and confirms the listing is an active Tashkent
  apartment for long-term rent on each detail response. Delivery carries the public website URL in `joymee.ad.v1`.
  Joymee captures run on an interval when enabled. Link availability checks are disabled for this reader.

---

## Wire

- Enums travel as camelCase labels (`ownFetch`, `pendingApproval`); durations as ISO 8601 (`PT1H`); nulls are omitted.
- Dictionary keys are camelCased on write (`JsonOptionsConstants.Default`): step keys are lowercase by rule, but any
  consumer-supplied dictionary (a subscription's `context`) loses a leading capital.
