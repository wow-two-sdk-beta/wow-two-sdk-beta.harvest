# wow-two-sdk-beta.harvest — Architecture

*Last updated: 2026-10-27*

One contract, two registrations, one service that hosts the first, and the engine lanes inside the module.

```
product Domain / Application ──► Abstractions (contract: registry broker, step and capability seams, envelope)
product composition root ──► Module (Form 1)  or  Client (Forms 2/3)
service/Harvest.Api ──► Module (ProjectReference), + API key access + HTTP mirror of the contract + console (apps/web)
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
| `packages/Testing` | registry, run, post and delivery fakes and their shared conformance suites |
| `service/Harvest.Api` | hosting, API key access (operator key, tenant scopes, container isolation), the HTTP mirror, the console |
| `fe-harvest` | the TypeScript contract mirror, registry and console clients (`./kit`), reusable management screens (`./admin`) |
| `apps/web` | the console (`/`, composing `fe-harvest/admin`), on `fe-harvest` through its exports |

- Lanes run in parallel on disjoint folders; the schema in the SQL migrations is their shared seam, and each lane's lead
  doc names the contract seam it implements and the tables it owns.
- Fetching is direct only, built inline in `Module/Capabilities/` over core `Http/Safety`. Harvest never escalates
  around a site's defences: a challenge, block or rate limit is an outcome, and the host backs off.
- Generic readers use AngleSharp and bounded XML/JSON parsers; extraction runs without model services. Captures and
  conditional-response replay bodies stay in PostgreSQL while Files integration is unbound.
- The service explicitly composes all engine lanes and opts into the worker. A library consumer can register the
  brokers without starting background work. `Harvest:Worker:Enabled=false` disables the service's worker.
- The private adapters repository references public contracts/packages and builds WoW2's actual source-specific
  images. This repository's self-hosted image remains generic; no named-source implementation was added here.
