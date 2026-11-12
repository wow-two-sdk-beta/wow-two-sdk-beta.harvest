# WoW2 SCFD

SCFD (Source Collection and Feed Delivery) captures and deduplicates posts from web, feed and named sources, checks
for deletions and delivers updates to subscribing products. Afisha, ANONS, CAEx, Craftfair/Hunarmand and
Uzexpocentre supply event candidates for Ocharo; OLX supplies listings; Instagram is an opt-in source.
[Brand and integration names](product/brand/brand.md)

Sourcing serves the subscribing products' internal tracking only: nothing collected is republished, and anything shown
carries facts plus an attributed link to the original (events-integration #50).

## Artifacts

| Artifact | Registry | Line · tag | Install |
|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Harvest.Abstractions` — the contract | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Abstractions --prerelease` |
| `WoW2.Sdk.Beta.Be.Harvest.Module` — Form 1, in process | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Module --prerelease` |
| `WoW2.Sdk.Beta.Be.Harvest.Client` — Forms 2 and 3, over HTTP | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Client --prerelease` |
| `WoW2.Sdk.Beta.Be.Harvest.Testing` — fake and conformance suite | nuget.org | `10.y.z-beta` · `be-v*` | `dotnet add package WoW2.Sdk.Beta.Be.Harvest.Testing --prerelease` |
| `@wow-two-beta/fe-harvest` — contract mirror, registry client, admin panels | npm | `0.0.z` · `fe-v*` | `pnpm add @wow-two-beta/fe-harvest` |
| `harvest` service image — engine, API and source readers | GHCR | `X.Y.Z` · `v*` | `ghcr.io/wow-two-sdk-beta/wow-two-sdk-beta.harvest/harvest`, deployed by Wheelhouse |

- A product consuming the NuGet family pins it with one `HarvestSdkVersion` property beside `BackendSdkVersion`.
- The source readers under `sources/` are not packages; they ship only inside the image.

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

// Forms 2 and 3 use an SCFD service with an API key or a client-credentials token.
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

## Harvesting

Generic readers cover sitemaps, JSON-LD events, RSS/Atom, ICS and ordinary pages. The named source readers add
Afisha, ANONS, CAEx, Craftfair, Uzexpocentre, OLX and Instagram. Extraction is deterministic; captures are immutable
with field provenance; duplicates merge; known links are checked; delivery is resumable pull. The API manages
sources, pipelines, runs and subscriptions and exposes posts and host health.

Fetching uses the host's own connection, a transparent Harvest user agent, `robots.txt`, per-host pacing and
conditional requests. Challenges, blocks and rate limits become recorded outcomes and persistent pauses. Proxy/IP,
supplied account-session, CAPTCHA and Instagram integrations ship in the image and stay disabled until explicitly
configured; a configured route keeps the same robots, permission, pacing, redirect and refusal policies.

`GET /api/sources/{sourceId}/capabilities` describes the installed reader, supported operations, local readiness and
saved scope schema under the source's tenant visibility rules; only display-safe metadata crosses
`ISourceConfigurationProvider`.

Pipelines store a starting URL with search filters, query overrides and per-kind cadence. Interval, cron, adaptive
and manual modes support IANA timezones and optional active-hour windows. Disabled schedules stay manual-only.
`PUT api/pipelines/{id}` replaces the name, complete scope and schedule list and keeps the pipeline's identity,
lifecycle, limits, step overrides, schedule IDs and run history; queued runs keep their frozen scope.

Ocharo's sources are provisioned explicitly: `POST /api/ocharo/sources/setup` or the host verb
`ocharo setup --group <id>` creates or reuses the tenant, container, pipelines and filtered `wow2.event.v1`
subscriptions, schedules off unless asked. [Provisioning](engineering/deployment/deployment.md#provisioning-ocharo-on-a-target)

## Layout

```
product/                 ← the capability: spec, state, features, flows, brand (no code)
engineering/             ← the how: architecture (contract, package registry), planning, deployment, research, scripts
└── codebase/
    ├── harvest.backend-services/    ← packages/ (NuGet) · sources/ (readers, image only) · service/ · tests/ · tools/
    └── harvest.frontend-services/   ← packages/fe-harvest (npm) · standalone demo
```

## Run it (dev)

```bash
# Detached local stack: PostgreSQL and HarvestSources.Api on http://127.0.0.1:8397. First copy .env.example to .env
# beside it and fill the database password and an operator key (hv_ + 32 letters and digits).
cd engineering/deployment && docker compose up -d --build --wait

# Source host from an IDE shell (the https launch profile sets LOCAL_SETTINGS=true, so appsettings.Local.json applies:
# DatabaseSettings:ConnectionString and Harvest:Operator:ApiKey); https://localhost:8400
cd engineering/codebase/harvest.backend-services && dotnet run --project service/HarvestSources.Api --launch-profile https
```

`pnpm dev:demo` in the frontend workspace opens a standalone demo with synthetic pipelines; `pnpm dev:http` with
`VITE_API_TARGET` points the console at a local API. [Machine access and deployment](engineering/deployment/deployment.md)

## Test it

```bash
# Backend: engine tiers (unit, integration, E2E, conformance, migrations) and source tiers (HarvestAdapters unit and
# integration, HarvestSources E2E); the database tiers run postgres:18-alpine in Testcontainers (Docker).
cd engineering/codebase/harvest.backend-services && dotnet test SCFD.BackendServices.slnx

# OLX live-check tool: offline tests
cd engineering/codebase/harvest.backend-services && python3 -m unittest discover -s tools/tests

# Frontend: lint, tests (builds fe-harvest first), production build, packed-consumer check
cd engineering/codebase/harvest.frontend-services && pnpm lint && pnpm test && pnpm build && pnpm check:package

# Release scripts: the tag version logic
bash engineering/scripts/test-next-version.sh
```

## Pointers

- Capability spec: `product/product.md` · state and decisions: `product/context.md`
- Contract: `engineering/architecture/contract.md` · packages: `engineering/architecture/package-registry.md`
- Releases: `engineering/scripts/scripts.md` · deployment: `engineering/deployment/deployment.md`
- Source and provider evidence: `engineering/research/research.md`
- Plan: `engineering/planning/backlog.md` · active version: newest `engineering/planning/version-track/v{X.Y}/`
- Conventions: `wow-two-ws/conventions/development/repo/structure/domain-structure.md`
