# wow-two-sdk-beta.harvest — Backend Development Guidelines

Defer to wow-two-ws/conventions/development/backend/ and the domain repo shape in
wow-two-ws/conventions/development/repo/structure/domain-structure.md. Repo deltas:

- `packages/*` are the NuGet family: packable, XML docs, warnings as errors, a `README.md` beside each csproj (all set
  by path in `Directory.Build.props`). `sources/*` get XML docs and warnings as errors but are not packable;
  `service/` and `tests/` are not packable.
- A new package is a `packages/{Layer}/WoW.Two.Sdk.Beta.Be.Harvest.{Layer}.csproj`; its id drops `.Two` (`WoW2.…`), its
  namespaces root at `WoW.Two.Sdk.Beta.Be.Harvest`, and its row joins `engineering/architecture/package-registry.md`.
- A new source is a `sources/{Source}/WoW.Two.Sdk.Beta.Be.HarvestAdapters.{Source}.csproj` referencing
  `packages/Abstractions` and its parser only, with an adapter and a capture reader that make no network call and
  execute no page script. `sources/Module` registers it (`AddHarvestWebBroker<TAdapter,TReader>`); its lead doc
  `{source}.md` states its discovery scope and evidence limits, and its row joins package-registry § Source readers.
- Runtime, store and API changes belong in `packages/`; a source needing one fixes the package rather than working
  around it. Source readers add no schema.
- The contract (`Abstractions`) holds no DI registration, options binding, logic or I/O; a new broker member gets a
  conformance case in `packages/Testing` that every implementation passes, the fake included.
- Store: PostgreSQL 16+ through `DatabaseSettings:ConnectionString` (`DB_CONNECTION` wins); the database must be UTF-8
  and offer `pg_trgm`, or boot refuses it. Tables carry the `harvest_` prefix, snake_case columns and enum text that
  the SQL CHECKs name; the module keeps its own keyed Npgsql data source and its migration set `harvest`.
- A new table is a new `packages/Module/Migrations/NNN-name/{Apply,Rollback}.sql` pair; never edit an applied one.
  The EF context maps over the SQL and never creates it.
- A lane works in its `packages/Module/{Lane}/` folder and maps the tables its lead doc names; a schema change another
  lane needs is a new migration, agreed in the contract first.
- `dotnet test SCFD.BackendServices.slnx` runs every tier, the source tiers included; the database tiers need Docker,
  and the backend build needs Node and pnpm for the pages. Fixtures and fake providers make no live request.
- A local run sets its connection string and the operator key (`Harvest:Operator:ApiKey`, `hv_` + 32 letters and
  digits) in `service/HarvestSources.Api/appsettings.Local.json` (https `8400`, http `8401`), which never leaves the
  machine. `VITE_API_TARGET=https://localhost:8400` points `pnpm dev`'s proxy at it.
