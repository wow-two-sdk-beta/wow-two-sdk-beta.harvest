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
| `packages/Testing` | `FakeRegistrationBroker` and `RegistrationBrokerConformance` |
| `service/Harvest.Api` | hosting, API key access (operator key, tenant scopes, container isolation), the HTTP mirror, the console |
| `fe-harvest` | the TypeScript contract mirror, the registry client (`./kit`), the management screens (`./admin`) |
| `apps/web` | the console (`/`, composing `fe-harvest/admin`), on `fe-harvest` through its exports |

- Lanes run in parallel on disjoint folders; the schema in SQL `001` is their shared seam, and each lane's lead doc
  names the contract seam it implements and the tables it owns.
- Egress and sessions are built inline in `Module/Capabilities/` behind core- and Social-shaped contracts, and move out
  at the next +0.1 by a namespace change.
