# wow-two-sdk-beta.harvest — Architecture

*Last updated: 2026-10-27*

One contract, two registrations, one service that hosts the first, and the engine lanes inside the module.

```
product Domain / Application ──► Abstractions (contract: registry broker, step and capability seams, envelope)
product composition root ──► Module (Form 1)  or  Client (Forms 2/3)
service/Harvest.Api ──► Hosting ──► Module; adds the console assets (apps/web)
Client ──HTTP + X-Api-Key──► service/Harvest.Api ──► Module ──► PostgreSQL (harvest_ tables, migration set harvest)
private adapters repo ──► Abstractions (ISourceAdapter, IStepBroker) + core; registered in code by the host
```

| Form | Registration | Store owner |
|---|---|---|
| 1 · library | `services.AddHarvest()` + `MigrateHarvestOnStartupAsync()` | the consumer's PostgreSQL |
| 2 · self-hosted | `services.AddHarvestClient(…)` + a `harvest` container the consumer runs | the consumer's container |
| 3 · provided | `services.AddHarvestClient(…)` | the central instance |

| Project | Responsibility |
|---|---|
| `packages/Abstractions` | the contract — `IRegistrationBroker`, the step-broker and capability seams, captures, `wow2.post.v1`, delivery types; no DI, options or I/O |
| `packages/Module` | Form 1: the store (own keyed Npgsql data source), the migration set and boot self-test, the registry broker, the API key store; lane folders `Capabilities/` · `Engine/` · `Readers/` · `Extraction/` · `Posts/` · `Delivery/` |
| `packages/Client` | Forms 2/3 over the service's HTTP API, authenticated by an API key |
| `packages/Hosting` | shared ASP.NET startup, API key policies and HTTP controllers for generic and private adapter hosts |
| `packages/Testing` | registry, run, post and delivery fakes and their shared conformance suites |
| `service/Harvest.Api` | hosting, API key access (operator key, tenant scopes, container isolation), the HTTP mirror, the console |
| `fe-harvest` | the TypeScript contract mirror, registry and console clients (`./kit`), reusable management screens (`./admin`) |
| `apps/web` | the console (`/`, composing `fe-harvest/admin`), on `fe-harvest` through its exports |

- Lanes run in parallel on disjoint folders; the schema in the SQL migrations is their shared seam, and each lane's lead
  doc names the contract seam it implements and the tables it owns.
- Fetching defaults to direct HTTP in `Module/Capabilities/` over core `Http/Safety`. An explicit
  `IFetchTransportFactory` supplies a request-local route lease for private proxy/session integrations. Permission,
  pacing, robots and same-origin redirects still run; unavailable routes fail closed. Private routes disable shared
  response caching. Challenges, blocks and rate limits remain outcomes that back off the host.
- Generic readers use AngleSharp and bounded XML/JSON parsers; extraction runs without model services. Captures and
  conditional-response replay bodies stay in PostgreSQL while Files integration is unbound.
- Hosting explicitly composes all engine lanes and opts into the worker. A library consumer can register the
  brokers without starting background work. `Harvest:Worker:Enabled=false` disables the service's worker.
- The private adapters repository references public contracts/packages and builds WoW2's actual source-specific
  images. This repository's self-hosted image remains generic; no named-source implementation was added here.
