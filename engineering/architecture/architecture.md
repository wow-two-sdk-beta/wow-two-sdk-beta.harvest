# SCFD architecture

*Last updated: 2026-11-12*

One contract, two registrations, one deployed host that composes the module with the source readers, and the engine
lanes inside the module.

```
product Domain / Application ──► Abstractions (contract: registry broker, step and capability seams, envelope)
product composition root ──► Module (Form 1)  or  Client (Forms 2/3)
service/HarvestSources.Api ──► Hosting ──► Module ──► PostgreSQL (harvest_ tables, migration set harvest)
                           ──► sources/Module ──► Ocharo: Afisha, Anons, Caex, Craftfair, Uzexpocentre
                                              ──► Marketplace: Olx
                           ──► sources/Instagram (opt-in) · sources/Integrations (opt-in: leased routes, supplied sessions, solver client)
service/Harvest.Api ──► Hosting ──► Module      (generic host; the E2E and Conformance tiers only)
Client ──HTTP + token or X-Api-Key──► service/HarvestSources.Api
```

| Form | Registration | Store owner |
|---|---|---|
| 1 · library | `services.AddHarvest()` + `MigrateHarvestOnStartupAsync()` | the consumer's PostgreSQL |
| 2 · self-hosted | `services.AddHarvestClient(…)` + a `harvest` container the consumer runs | the consumer's container |
| 3 · provided | `services.AddHarvestClient(…)` | the central instance |

| Project | Responsibility |
|---|---|
| `packages/Abstractions` | the contract — `IRegistrationBroker`, the step-broker and capability seams, captures, `wow2.post.v1`, delivery types; no DI, options or I/O |
| `packages/Module` | Form 1: the store (own keyed Npgsql data source), the migration set and boot self-test, the registry broker, the API key store; lane folders `Capabilities/` · `Engine/` · `Readers/` · `Extraction/` · `Posts/` · `Delivery/` · `Sources/` |
| `packages/Client` | Forms 2/3 over the service's HTTP API, authenticated by a service token or an API key |
| `packages/Hosting` | shared ASP.NET startup, access policies and HTTP controllers both hosts compose |
| `packages/Testing` | registry, run, post and delivery fakes and their shared conformance suites |
| `sources/{Afisha,Anons,Caex,Craftfair,Olx,Uzexpocentre}` | one adapter and capture reader per named source over `Abstractions` and AngleSharp; no network I/O |
| `sources/Instagram` · `sources/Integrations` | opt-in Business Discovery client; proxy/IP lease, supplied-session and 2Captcha clients; disabled by default |
| `sources/Module` | composition over `packages/Module`: the Ocharo registry, provisioner and `ocharo-fashion-v1` filter; the marketplace (OLX) |
| `service/HarvestSources.Api` | the deployed host: Hosting, the source composition, the Ocharo routes and the `ocharo` host verb |
| `service/Harvest.Api` | the generic host with scoped service tokens, local operator key access and the console; tests only |
| `fe-harvest` | the TypeScript contract mirror, registry and console clients (`./kit`), reusable management screens (`./admin`) |
| `apps/web` | standalone demo using the `fe-harvest/admin` export |

- Lanes run in parallel on disjoint folders; the schema in the SQL migrations is their shared seam, and each lane's lead
  doc names the contract seam it implements and the tables it owns. The source readers add no schema.
- Fetching defaults to direct HTTP in `Module/Capabilities/` over core `Http/Safety`. An explicit
  `IFetchTransportFactory` supplies a request-local route lease for configured proxy/session integrations. Permission,
  pacing, robots and same-origin redirects still run; unavailable routes fail closed. Routed traffic disables shared
  response caching. Challenges, blocks and rate limits remain outcomes that back off the host.
- Generic readers use AngleSharp and bounded XML/JSON parsers; extraction runs without model services. Captures and
  conditional-response replay bodies stay in PostgreSQL while Files integration is unbound.
- Hosting explicitly composes all engine lanes and opts into the worker. A library consumer can register the
  brokers without starting background work. `Harvest:Worker:Enabled=false` disables the service's worker.

## Source host

- `HarvestSources.Api` runs Hosting's startup, migrations and API, then registers the sources after the generic
  readers. `AddHarvestWebBroker<TAdapter,TReader>` binds each named adapter to the generic web step engine and gives
  its reader precedence. No reader makes network calls or executes page scripts.
- `HarvestSources:Audience` picks the composition: `internal` (default) adds the sources, provider settings and the
  Ocharo routes; `public` hosts the generic engine and API only.
- `OcharoSourceProvisioner` is an explicit scoped operation: it creates or reuses a tenant, its private container,
  pipelines and filtered subscriptions. Setup calls serialize within one process, so multiple replicas need one
  setup-owning host. Store conflicts stay visible and a partial setup can be retried.
- `IDeliveryPostFilter` is a host seam. `OcharoFashionFilter` requires an event plus multilingual subject or
  opportunity evidence. An unknown or throwing predicate fails closed for new candidates; raw captures and post
  lifecycle history survive.
- `AddHarvestIntegrations` replaces `IFetchTransportFactory` only when configured. A lease holds the HTTP client, its
  exact origin, cookie jar and IP/account reservation. Provider clients receive no source authentication. Session
  exclusivity is per process, so one session must not be enabled in several hosts. The backend SDK validates proxy
  endpoints and opens guarded sockets.
- The 2Captcha client is injectable and bounded; it never resubmits or applies a solved token to an arbitrary page.
  No named source's challenge protocol is implemented.
- Named adapters implement `ISourceConfigurationProvider`: labels, scope schemas and local readiness travel through
  the authorized source-capabilities API; parser and provider implementations and credential values do not.
  Instagram reports configuration-required while its connection is disabled or incomplete.
