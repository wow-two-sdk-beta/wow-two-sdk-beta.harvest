# File References

> **Lookup table only.** Do NOT read files proactively — only when the current task requires them.
> **Tracks docs outside `engineering/codebase/`.** Source files are navigated via `tree`/`find`/`grep`, never listed here.

| Question about | Read |
|---|---|
| The capability — problem, forms, model, phases | `product/product.md` |
| Name and one-line description (read by consoles) | `product/product.json` |
| State, tasks, decisions | `product/context.md` |
| Feature list + state | `engineering/planning/backlog.md` § Features |
| One feature's spec | `product/features/{feature}.md` |
| User / product flows | `product/flows/flows.md` |
| Hosted pages' brand | `product/brand/brand.md` |
| Technical overview · stack · map | `engineering/engineering.md` |
| System design — forms map, package graph, service role | `engineering/architecture/architecture.md` |
| The broker contract, every form | `engineering/architecture/contract.md` |
| Packages, layers, consumers | `engineering/architecture/package-registry.md` |
| Runtime composition — standalone UI, capability discovery, artifact boundary | `engineering/architecture/composition.md` |
| Architecture decisions | `engineering/architecture/decisions/decisions.md` |
| What lives where in the code | `engineering/codebase/codebase.md` |
| Backend dev guidelines | `engineering/development/backend-guidelines.md` |
| Frontend dev guidelines | `engineering/development/frontend-guidelines.md` |
| Iteration / version workflow | `engineering/development/iteration-guide.md` |
| Backlog — every unbuilt item | `engineering/planning/backlog.md` |
| Active version | newest `engineering/planning/version-track/v{X.Y}/v{X.Y}.md` |
| Working rules for agents | `engineering/development/rules.md` |
| Repository — how it was created, GitHub settings, rollout state | `engineering/development/development.md` § Repository |
| Deploy / ops — local Compose, service access, Ocharo provisioning, integrations | `engineering/deployment/deployment.md` |
| Local Compose variables | `engineering/deployment/.env.example` |
| What deploys — service, sites, settings, dev check | `engineering/deployment/deploy.yml` |
| Releases — versions, tags, verification | `engineering/scripts/scripts.md` |
| Source and provider reviews, research batches | `engineering/research/research.md` |
| Ocharo publisher and channel inventory | `engineering/research/ocharo-source-registry.md` |

## Source projects (`engineering/codebase/`)

> Individual files NOT listed — use `tree`/`find`/`grep`. Describe projects only.

### `codebase/harvest.backend-services/`
| Project | What it is |
|---|---|
| `packages/Abstractions` | the contract: `IRegistrationBroker`, the step-broker and capability seams, `Capture`, `wow2.post.v1`, delivery types |
| `packages/Module` | Form 1: the store on PostgreSQL, `AddHarvest()`, the SQL migration set and boot self-test, the lane folders |
| `packages/Client` | Forms 2/3: the broker over the service's HTTP API, `AddHarvestClient()` |
| `packages/Testing` | `FakeRegistrationBroker` and the conformance suite |
| `packages/Hosting` | shared startup, access policies and HTTP controllers both hosts compose |
| `sources/{Afisha,Anons,Caex,Craftfair,Olx,Uzexpocentre}` | named source readers: adapter, capture reader, no network I/O |
| `sources/Instagram` | opt-in Business Discovery client and source broker, disabled by default |
| `sources/Integrations` | opt-in proxy/IP lease, supplied-session and CAPTCHA clients, disabled by default |
| `sources/Module` | source composition: the Ocharo registry, preset, provisioner and `ocharo-fashion-v1` filter; the marketplace (OLX) |
| `service/HarvestSources.Api` | the deployed host: Hosting, the source readers, the Ocharo routes and the `ocharo` host verb; serves no console |
| `tests/Harvest.Tests.{Unit,Integration,E2E,Conformance,Migrations}` | the engine test tiers |
| `tests/HarvestAdapters.Tests.{Unit,Integration}` · `tests/HarvestSources.Tests.E2E` | the source tiers: reader fixtures, pipelines over PostgreSQL, the source host and its verbs |
| `tests/Contract` | golden `wow2.event.v1` pull pages Events replays, written and checked by the E2E tier |
| `tools/verify-olx-live.py` | a separately requested, bounded OLX capture against a local source host |

### `codebase/harvest.frontend-services/`
| Project | What it is |
|---|---|
| `packages/fe-harvest` | `@wow-two-beta/fe-harvest`: `./contracts` mirror, `./kit` (`createRegistryClient`, `createHarvestClient`), `./admin` (`HarvestConsoleProvider` and one panel per console page) |
| `apps/web` | the console: one route per page under `AppLayout`, composing `./admin`; locally it proxies `/api` to `HarvestSources.Api` |
