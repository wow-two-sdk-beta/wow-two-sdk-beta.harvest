# wow-two-sdk-beta.harvest

Harvest: one 24/7 harvester that captures posts from web, feed and social sources, merges duplicates, checks deletions and delivers them to subscribing products.

## Artifacts

| Artifact | Registry | Line · tag | Install |
|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Harvest.Abstractions` — the contract | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Abstractions --prerelease` |
| `WoW2.Sdk.Beta.Be.Harvest.Module` — Form 1, in process | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Module --prerelease` |
| `WoW2.Sdk.Beta.Be.Harvest.Client` — Forms 2 and 3, over HTTP | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Client --prerelease` |
| `WoW2.Sdk.Beta.Be.Harvest.Testing` — fake and conformance suite | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Testing --prerelease` |
| `@wow-two-beta/fe-harvest` — contract mirror, registry client, admin panels | npm | `0.0.z` · `fe-v*` | `pnpm add @wow-two-beta/fe-harvest` |
| `harvest` service image | GHCR | `X.Y.Z` · `v*` | `ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.harvest/harvest`, deployed by Wheelhouse |

A product pins the family with one `HarvestSdkVersion` property beside `BackendSdkVersion`.

## Forms

```csharp
// Form 1 — in process, over the product's own PostgreSQL (UTF-8, pg_trgm); boot creates, checks and migrates it
services.AddHarvest().Configure(options => options.ConnectionString = "...");
services.AddHarvestFetching();
services.AddHarvestReaders();
services.AddHarvestExtraction();
services.AddHarvestPosts();
services.AddHarvestDelivery();
services.AddHarvestEngine();
services.AddHarvestWorker(); // explicit opt-in to background schedules
await app.Services.MigrateHarvestOnStartupAsync();

// Forms 2 and 3 — over the running Harvest service with an API key; the product's code does not change
services.AddHarvestClient(options =>
{
    options.BaseAddress = new Uri("https://harvest.example.com/");
    options.ApiKey = "hv_…";
});
services.AddHarvestDataClient(); // IPostBroker and IDeliveryBroker
services.AddHarvestRunClient();  // IRunBroker
```

The registration extensions live in `Module.Capabilities`, `Module.Readers`, `Module.Extraction`, `Module.Posts`,
`Module.Delivery`, `Module.Engine`, `Client.Delivery` and `Client.Runs`, under `WoW.Two.Sdk.Beta.Be.Harvest`.

## Direct harvesting

The generic service reads sitemaps, JSON-LD events, RSS/Atom, ICS and ordinary pages. It runs deterministic extraction,
keeps immutable captures and field provenance, merges duplicates, checks known links and offers resumable pull delivery.
The reusable console manages sources, pipelines, runs and subscriptions and shows posts and host health.

Fetching uses the host's own network connection, a transparent Harvest user agent, robots policy, per-host pacing and
conditional requests. Challenges, blocks and rate limits become recorded outcomes and persistent pauses. No CAPTCHA,
proxy/IP, account-pool, browser-renderer, AI-provider or named-source integration is required by the generic host.
Private hosts can replace `IFetchTransportFactory` with explicitly configured route/session leases; the same robots,
permission, pacing, redirect and refusal policies still apply. Provider clients and named sources live in the private
adapters repository.

Runtime requirements are PostgreSQL 16+ with UTF-8 and `pg_trgm`, the database connection and an operator API key.
The service starts the worker by default; set `Harvest:Worker:Enabled=false` for a management-only host.
`Harvest:Fetch` binds `HarvestFetchOptions`, including its user agent, deadline and response-size limit.

`WoW2.Sdk.Beta.Be.Harvest.Hosting` provides the reusable API controllers, authentication and startup composition.
An adapter host references that package and registers its parsers and providers after `builder.Configure()`.

The browser keeps its API key in memory. A disconnected console makes no authenticated registry requests.

The complete console belongs to this public repository. `pnpm dev:demo` in the frontend workspace opens synthetic,
editable pipelines without a backend, private checkout or source connections. `VITE_API_TARGET` selects a real
runtime during development. [Composition and standalone UI development](engineering/architecture/composition.md).

`GET /api/sources/{sourceId}/capabilities` describes the installed adapter, supported operations, local readiness
and saved scope schema under the source's tenant visibility rules. Adapters optionally implement
`ISourceConfigurationProvider`; only safe display metadata crosses this seam.

The console uses a responsive sidebar with a pipeline list. Select a pipeline to edit its starting URL (including
search filters), query parameter overrides and per-kind cadence. Interval, cron, adaptive and manual modes support
IANA timezones and optional active-hour windows. Disabled schedules stay manual-only. Collection still requires a
matching adapter; saving a URL does not establish that the source can be read.

`IRegistrationBroker.UpdatePipelineAsync(id, update)` / `PUT api/pipelines/{id}` replaces the name, complete scope and
schedule list. It preserves the pipeline/source identity, lifecycle, limits, step overrides, schedule IDs and run
history. Omitted work kinds are disabled. Queued runs keep their frozen scope and cadence; future runs use the edit.

## Layout

```
product/                 ← the capability: spec, state, features, flows, brand (no code)
engineering/             ← the how: architecture (contract, package registry), planning, deployment, scripts
└── codebase/
    ├── harvest.backend-services/    ← packages/ (the NuGet family) · service/Harvest.Api · tests/
    └── harvest.frontend-services/   ← packages/fe-harvest (npm) · apps/web (console)
```

## Run it (dev)

```bash
# Service (the https launch profile sets LOCAL_SETTINGS=true, so appsettings.Local.json applies): it holds the
# PostgreSQL connection string (DatabaseSettings:ConnectionString) and the operator key (Harvest:Operator:ApiKey)
cd engineering/codebase/harvest.backend-services && dotnet run --project service/Harvest.Api --launch-profile https

# Pages and console, proxied to the service
cd engineering/codebase/harvest.frontend-services && pnpm install --frozen-lockfile && pnpm dev
```

## Test it

```bash
# Backend: unit, integration, E2E, conformance and migration tiers; the database tiers run postgres:18-alpine in
# Testcontainers (Docker), and the build also builds the frontend workspace, so Node and pnpm are required
cd engineering/codebase/harvest.backend-services && dotnet test Harvest.BackendServices.slnx

# Frontend: lint, tests (builds fe-harvest first), production build, packed-consumer check
cd engineering/codebase/harvest.frontend-services && pnpm lint && pnpm test && pnpm build && pnpm check:package

# Release scripts: the tag version logic
bash engineering/scripts/test-next-version.sh
```

## Pointers

- Capability spec: `product/product.md` · state and decisions: `product/context.md`
- Contract: `engineering/architecture/contract.md` · packages: `engineering/architecture/package-registry.md`
- Releases: `engineering/scripts/scripts.md` · deployment: `engineering/deployment/deployment.md`
- Plan: `engineering/planning/backlog.md` · active version: newest `engineering/planning/version-track/v{X.Y}/`
- Conventions: `wow-two-ws/conventions/development/repo/structure/domain-structure.md`
