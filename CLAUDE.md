# wow-two-sdk-beta.harvest

WoW2 SCFD (Source Collection and Feed Delivery) reads its named sources and generic web feeds, deduplicates posts,
checks for deletions and delivers updates to subscribing products. Sourcing serves internal tracking only; nothing
collected is republished (events-integration #50).

## Lazy loading

- Do NOT pre-read files at conversation start; open a file only when the task needs it.
- `.claude/rules/file-references.md` is a lookup table, not a reading list.
- Navigate code under `engineering/codebase/` with `tree`/`find`/`grep` — it is not listed in references.

## Structure

```
product/                 ← the capability (no code): product · context · features · flows · brand
engineering/             ← the HOW
├── architecture/        ← architecture · contract · package-registry · composition · decisions/ · flows/
├── development/         ← this repo's dev guidelines (defer to wow-two-ws/conventions/)
├── planning/            ← backlog.md + version-track/v{X.Y}/ (newest = active; drives the image version only)
├── deployment/          ← deploy.yml · Dockerfile · docker-compose.yml · .env.example · deployment.md
├── research/ · scripts/ ← source and provider reviews · release helpers publish-packages.yml calls
└── codebase/            ← THE CODE — only place code lives
    ├── harvest.backend-services/    ← SCFD.BackendServices.slnx: packages/{Abstractions,Module,Client,Testing,Hosting} (NuGet) · sources/{Afisha,Anons,Caex,Craftfair,Instagram,Integrations,Olx,Uzexpocentre,Module} (image only) · service/HarvestSources.Api · tests/ · tools/
    └── harvest.frontend-services/   ← pnpm workspace: packages/fe-harvest (npm: contracts · kit · admin) · standalone demo
```

## Rules

- Shape: wow-two-ws/conventions/development/repo/structure/domain-structure.md; repo-specific deltas only in `engineering/development/`.
- The contract lives in `packages/Abstractions`; Module (Form 1) and Client (Forms 2/3) implement it, and
  `Harvest.Tests.Conformance` runs the one suite (`RegistrationBrokerConformance`, in `packages/Testing`) against both
  and the fake. The engine seams and the lane that implements each: `engineering/architecture/contract.md`.
- Each engine lane works in its `packages/Module/{Lane}/` folder, whose lead doc names its seam and its tables.
- A package references only what its layer row allows. Hosting supplies shared startup, machine access and the HTTP
  mirror for both hosts.
- `service/HarvestSources.Api` is the one deployed host: Hosting, `sources/Module`, the Ocharo routes and the `ocharo`
  host verb; the E2E and Conformance tiers boot it too.
- `sources/*` are not packable and ship only inside the image. Each reader references `packages/Abstractions` and its
  parser and performs no network I/O; `sources/Module` composes them over `packages/Module`.
- Source posture: read directly, identified, inside `robots.txt` and paced; a challenge, block or rate limit pauses
  the source. Proxy/IP, CAPTCHA, account-session and Instagram integrations stay disabled until explicitly configured.
- Management screens ship in `fe-harvest`'s `./admin` export; `apps/web`'s console composes them and builds none itself.
- Standalone UI work uses `pnpm dev:demo` with synthetic client data. `VITE_API_TARGET` selects a local API key host.
- Source capability discovery returns declarative metadata through `ISourceConfigurationProvider`; no reader
  implementation or credential enters the UI package. See `engineering/architecture/composition.md`.
- Package versions come from tags (`be-v*` NuGet, `fe-v*` npm) in `publish-packages.yml`; never commit a version,
  never create a GitHub Release for a package tag. The image version comes from the version track (`v*`).
- Core is pinned (`BackendSdkVersion`, the exact `@wow-two-beta/fe`); a needed core change goes to core first, then the
  pin moves here. `core-latest.yml` tests the newest core nightly; a red run is a fix here, not a gate.
- Schema changes are `packages/Module/Migrations/NNN-name/{Apply,Rollback}.sql` pairs; never edit an applied one.
  Source readers add no schema of their own.
- The store is PostgreSQL with UTF-8 and `pg_trgm`. Integration, Migrations, Conformance and both E2E tiers run
  `postgres:18-alpine` in Testcontainers and require Docker.
- Repository creation, GitHub settings, rollout state and hand steps: `engineering/development/development.md`
  § Repository.
