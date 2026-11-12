# codebase/

The code lives in `harvest.backend-services/` (.NET) and `harvest.frontend-services/` (Vue).

## Backend

- `SCFD.BackendServices.slnx` groups `Packages/`, `Sources/`, `Services/` and `Tests/`.
- `packages/` holds the NuGet family `WoW2.Sdk.Beta.Be.Harvest.*`: `Abstractions` (the contract, by concept:
  `Registration`, `Tenants`, `Containers`, `Sources`, `Pipelines`, `Steps`, `Capabilities.*`, `Readers`, `Extraction`,
  `Posts`, `Runs`, `Delivery`), `Module` (Form 1, with `Migrations/NNN-name/{Apply,Rollback}.sql`), `Client` (Forms
  2/3), `Hosting` (shared startup and controllers) and `Testing` (fake + conformance suite). `Directory.Build.props`
  makes them packable by path; the core pin in `Directory.Packages.props` is their floor.
- `Module`: `Persistence/` (the context and the registry rows), `Store/` (the boot self-test), `Registration/` (the
  broker and its rules), `ApiKeys/` (the key store core's scheme reads), and the lane folders `Capabilities/`,
  `Engine/`, `Readers/`, `Extraction/`, `Posts/`, `Delivery/`, `Sources/` (the console's read side), each with its lead
  doc.
- `sources/` holds the named source readers `Afisha`, `Anons`, `Caex`, `Craftfair`, `Olx`, `Uzexpocentre`, the opt-in
  `Instagram` and `Integrations` clients, and `Module` (the Ocharo registry `Ocharo/ocharo-event-sources.json`,
  provisioner and filter; `Marketplace/` for OLX). Not packable: `Directory.Build.props` gives them XML docs and
  warnings as errors, and they ship only inside the image. Each folder's lead doc holds the source's reading rules.
- `service/HarvestSources.Api` is the deployed host: Hosting's startup and controllers, the sources, the Ocharo
  controller and the `ocharo` host verb; it serves no console. It is the only host, and the E2E and Conformance tiers
  boot it; Hosting owns the store boot, API controllers, scoped machine access and local development
  keys under `Registration/Access/`.
- Tests: `Harvest.Tests.Unit` (package logic, no I/O), `Integration` (the module over PostgreSQL: rows, self-test, key
  store, partitions), `E2E` (the generic host in process: boot and refusal, access, the wire, `/health`, the
  descriptor's dev check), `Conformance` (one suite against module, client and fake), `Migrations` (the module's SQL
  set). `HarvestAdapters.Tests.Unit` (readers against source fixtures, fake providers, the registry),
  `HarvestAdapters.Tests.Integration` (Ocharo and OLX pipelines over PostgreSQL) and `HarvestSources.Tests.E2E` (the
  source host: setup authority, capability metadata, the `ocharo` verbs, no console). The database tiers run
  `postgres:18-alpine` in Testcontainers through `tests/Shared/PostgresServer.cs` and `tests/Shared/SourcePostgres.cs`
  — one server per test assembly, a fresh database per fixture — so they need Docker. Run them with
  `dotnet test SCFD.BackendServices.slnx`; fixtures and fake providers make no live request.
- `tools/verify-olx-live.py`: a separately requested, bounded capture against a local source host (default
  `https://localhost:8400`); `--ca-file` trusts its development certificate, API redirects are refused and the
  operator key stays out of evidence and errors. Offline tests: `python3 -m unittest discover -s tools/tests`.

## Frontend

The pnpm workspace publishes `packages/fe-harvest` as `@wow-two-beta/fe-harvest` with `./contracts`, `./kit` and
`./admin` exports. `apps/web` is a standalone demo.

Rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md
