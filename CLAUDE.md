# wow-two-sdk-beta.harvest

Harvest: one 24/7 harvester that captures posts from web, feed and social sources, merges duplicates, checks deletions and delivers them to subscribing products.

## Lazy loading

- Do NOT pre-read files at conversation start; open a file only when the task needs it.
- `.claude/rules/file-references.md` is a lookup table, not a reading list.
- Navigate code under `engineering/codebase/` with `tree`/`find`/`grep` — it is not listed in references.

## Structure

```
product/                 ← the capability (no code): product · context · features · flows · brand
engineering/             ← the HOW
├── architecture/        ← architecture · contract · package-registry · decisions/ · flows/
├── development/         ← this repo's dev guidelines (defer to wow-two-ws/conventions/)
├── planning/            ← backlog.md + version-track/v{X.Y}/ (newest = active; drives the image version only)
├── deployment/          ← deploy.yml · Dockerfile · docker-compose.yml · deployment.md
├── research/ · scripts/ ← analyses · release helpers publish-packages.yml calls
└── codebase/            ← THE CODE — only place code lives
    ├── harvest.backend-services/    ← Harvest.BackendServices.slnx: packages/{Abstractions,Module,Client,Testing,Hosting} · service/Harvest.Api · tests/Harvest.Tests.*
    └── harvest.frontend-services/   ← pnpm workspace: packages/fe-harvest (npm: contracts · kit · admin) · apps/web (console)
```

## Rules

- Shape: wow-two-ws/conventions/development/repo/structure/domain-structure.md; repo-specific deltas only in `engineering/development/`.
- The contract lives in `packages/Abstractions`; Module (Form 1) and Client (Forms 2/3) implement it, and
  `Harvest.Tests.Conformance` runs the one suite (`RegistrationBrokerConformance`, in `packages/Testing`) against both
  and the fake. The engine seams and the lane that implements each: `engineering/architecture/contract.md`.
- Each engine lane works in its `packages/Module/{Lane}/` folder, whose lead doc names its seam and its tables.
- A package references only what its layer row allows. Hosting supplies shared startup, API key access and the HTTP
  mirror. The generic service and private adapter hosts reuse it; the service adds the console assets.
- Management screens ship in `fe-harvest`'s `./admin` export; `apps/web`'s console composes them and builds none itself.
- Package versions come from tags (`be-v*` NuGet, `fe-v*` npm) in `publish-packages.yml`; never commit a version,
  never create a GitHub Release for a package tag. The image version comes from the version track (`v*`).
- Core is pinned (`BackendSdkVersion`, the exact `@wow-two-beta/fe`); a needed core change goes to core first, then the
  pin moves here. `core-latest.yml` tests the newest core nightly; a red run is a fix here, not a gate.
- Schema changes are `packages/Module/Migrations/NNN-name/{Apply,Rollback}.sql` pairs; never edit an applied one.
- The store is PostgreSQL (UTF-8, `pg_trgm`; boot refuses anything else). The database tiers (Integration, Migrations,
  Conformance, E2E) run `postgres:18-alpine` in Testcontainers, so they need Docker; the backend build runs the
  frontend build (`BuildSpa`), so it needs Node and pnpm.
- Repository creation, GitHub settings, rollout state and hand steps: `engineering/development/development.md`
  § Repository.
