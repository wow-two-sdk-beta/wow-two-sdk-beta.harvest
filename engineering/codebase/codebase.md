# codebase/

The code lives in `harvest.backend-services/` (.NET) and `harvest.frontend-services/` (Vue).

## Backend

- `SCFD.BackendServices.slnx` groups `Packages/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Harvest.*`: `Abstractions` (the contract, by concept:
  `Registration`, `Tenants`, `Containers`, `Sources`, `Pipelines`, `Steps`, `Capabilities.*`, `Readers`, `Extraction`,
  `Posts`, `Runs`, `Delivery`), `Module` (Form 1, with `Migrations/NNN-name/{Apply,Rollback}.sql`), `Client` (Forms
  2/3) and `Testing` (fake + conformance suite). `Directory.Build.props` makes them packable by path; the core pin in
  `Directory.Packages.props` is their floor.
- `Module`: `Persistence/` (the context and the registry rows), `Store/` (the boot self-test), `Registration/` (the
  broker and its rules), `ApiKeys/` (the key store core's scheme reads), and the lane folders `Capabilities/`,
  `Engine/`, `Readers/`, `Extraction/`, `Posts/`, `Delivery/`, `Sources/` (the console's read side), each with its lead
  doc.
- `service/Harvest.Api` hosts the module by `ProjectReference`. Hosting owns the store boot, API controllers,
  scoped machine access and local development keys under `Registration/Access/`.
- Tests: `Unit` (package logic, no I/O), `Integration` (the module over PostgreSQL: rows, self-test, key store,
  partitions), `E2E` (the host in process: boot and refusal, access, the wire, `/health`, the descriptor's dev
  check), `Conformance` (one suite against module, client and fake), `Migrations` (the module's SQL set). The database
  tiers run `postgres:18-alpine` in Testcontainers through `tests/Shared/PostgresServer.cs` — one server per test
  assembly, a fresh database per fixture — so they need Docker. Run them with `dotnet test SCFD.BackendServices.slnx`.

## Frontend

The pnpm workspace publishes `packages/fe-harvest` as `@wow-two-beta/fe-harvest` with `./contracts`, `./kit` and
`./admin` exports. `apps/web` is a standalone demo.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md
