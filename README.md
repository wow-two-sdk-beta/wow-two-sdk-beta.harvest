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
await app.Services.MigrateHarvestOnStartupAsync();

// Forms 2 and 3 — over the running Harvest service with an API key; the product's code does not change
services.AddHarvestClient(options =>
{
    options.BaseAddress = new Uri("https://harvest.example.com/");
    options.ApiKey = "hv_…";
});
```

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
# Backend: unit, integration, E2E, conformance and migration tiers; the database tiers run postgres:16-alpine in
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
