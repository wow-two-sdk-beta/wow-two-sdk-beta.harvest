# codebase/

The code lives in `harvest.backend-services/` (.NET) and `harvest.frontend-services/` (Vue).

## Backend

- `Harvest.BackendServices.slnx` groups `Packages/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Harvest.*`: `Abstractions` (the contract), `Module` (Form 1,
  with `Migrations/NNN-name/{Apply,Rollback}.sql`), `Client` (Forms 2/3) and `Testing` (fake + conformance suite).
  `Directory.Build.props` makes them packable by path; the core pin in `Directory.Packages.props` is their floor.
- `service/Harvest.Api` hosts the module by `ProjectReference`: `AddApiDefaults()` / `UseApiDefaults()`, the migrator
  over the module's SQL, `GreetingsController` (the HTTP mirror of the contract), `SystemController`, and the pages in
  `wwwroot/` (filled by the `BuildSpa` target; `-p:BuildSpa=false` skips it).
- Tests: `Unit` (package logic), `Integration` (the module over a throwaway SQLite file), `E2E` (the host in process:
  wire, pages, `/health`, the descriptor's dev check), `Conformance` (one suite against module, client and fake),
  `Migrations` (the module's SQL). None needs Docker. Run them with `dotnet test Harvest.BackendServices.slnx`.

## Frontend

A pnpm workspace: `packages/fe-harvest` (`@wow-two-beta/fe-harvest`, published: `./contracts`, `./kit`, `./admin`) and
`apps/web` (hosted pages and the console that composes `./admin`, private). `scripts/deploy.mjs` copies
`apps/web/dist/` to `harvest.backend-services/service/Harvest.Api/wwwroot/`.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md
