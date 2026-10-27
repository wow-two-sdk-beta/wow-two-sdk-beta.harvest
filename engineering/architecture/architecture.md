# wow-two-sdk-beta.harvest — Architecture

*Last updated: 2026-10-27*

One contract, two registrations, one service that hosts the first.

```
product Domain / Application ──► Abstractions (contract)
product composition root ──► Module (Form 1)  or  Client (Forms 2/3)
service/Harvest.Api ──► Module (ProjectReference), + HTTP mirror of the contract + hosted pages (apps/web)
Client ──HTTP──► service/Harvest.Api ──► Module ──► the service's store
```

| Form | Registration | Store owner |
|---|---|---|
| 1 · library | `services.AddHarvest()` + the module's migrations | the consumer |
| 2 · self-hosted | `services.AddHarvestClient(…)` + a `harvest` container the consumer runs | the consumer's container |
| 3 · provided | `services.AddHarvestClient(…)` | the central instance |

| Project | Responsibility |
|---|---|
| `packages/Abstractions` | the contract — broker, model, constants, errors; no DI, options or I/O |
| `packages/Module` | Form 1 over the module's store; embedded SQL migrations |
| `packages/Client` | Forms 2/3 over the service's HTTP API |
| `packages/Testing` | the fake and the conformance suite |
| `service/Harvest.Api` | hosting, the HTTP mirror of the contract, the hosted pages and console |
| `fe-harvest` | the TypeScript contract mirror, the embeddable widget, the forward to the hosted page, the management screens (`./admin`) |
| `apps/web` | hosted pages (`/greet`) and console (`/`, composing `fe-harvest/admin`), on `fe-harvest` through its exports |
