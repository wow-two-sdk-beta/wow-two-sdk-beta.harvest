# codebase/

The code lives in `harvest.backend-services/` (.NET) and `harvest.frontend-services/` (Vue).

## Backend

- `Harvest.BackendServices.slnx` groups `Packages/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Harvest.*`: `Abstractions` (the contract, by concept:
  `Registration`, `Tenants`, `Containers`, `Sources`, `Pipelines`, `Steps`, `Capabilities.*`, `Readers`, `Extraction`,
  `Posts`, `Runs`, `Delivery`), `Module` (Form 1, with `Migrations/NNN-name/{Apply,Rollback}.sql`), `Client` (Forms
  2/3) and `Testing` (fake + conformance suite). `Directory.Build.props` makes them packable by path; the core pin in
  `Directory.Packages.props` is their floor.
- `Module`: `Persistence/` (the context and the registry rows), `Store/` (the boot self-test), `Registration/` (the
  broker and its rules), `ApiKeys/` (the key store core's scheme reads), and the lane folders `Capabilities/`,
  `Engine/`, `Readers/`, `Extraction/`, `Posts/`, `Delivery/`, each with its lead doc.
- `service/Harvest.Api` hosts the module by `ProjectReference`: `AddApiDefaults()` / `UseApiDefaults()`, the store boot
  (`MigrateHarvestOnStartupAsync`), API key access (`Registration/Access/`), the registration controllers, the
  `SystemController`, and the console in `wwwroot/` (filled by the `BuildSpa` target; `-p:BuildSpa=false` skips it).
- Tests: `Unit` (package logic, no I/O), `Integration` (the module over PostgreSQL: rows, self-test, key store,
  partitions), `E2E` (the host in process: boot and refusal, access, the wire, pages, `/health`, the descriptor's dev
  check), `Conformance` (one suite against module, client and fake), `Migrations` (the module's SQL set). The database
  tiers run `postgres:16-alpine` in Testcontainers through `tests/Shared/PostgresServer.cs` — one server per test
  assembly, a fresh database per fixture — so they need Docker. Run them with `dotnet test Harvest.BackendServices.slnx`.

## Frontend

A pnpm workspace: `packages/fe-harvest` (`@wow-two-beta/fe-harvest`, published: `./contracts`, `./kit`, `./admin`) and
`apps/web` (the console that composes `./admin`, private). `scripts/deploy.mjs` copies `apps/web/dist/` to
`harvest.backend-services/service/Harvest.Api/wwwroot/`.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md
