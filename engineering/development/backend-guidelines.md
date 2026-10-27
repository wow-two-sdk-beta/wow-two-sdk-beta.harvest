# wow-two-sdk-beta.harvest — Backend Development Guidelines

Defer to wow-two-ws/conventions/development/backend/ and the domain repo shape in
wow-two-ws/conventions/development/repo/structure/domain-structure.md. Repo deltas:

- `packages/*` are the NuGet family: packable, XML docs, warnings as errors, a `README.md` beside each csproj (all set
  by path in `Directory.Build.props`). `service/` and `tests/` are not packable.
- A new package is a `packages/{Layer}/WoW.Two.Sdk.Beta.Be.Harvest.{Layer}.csproj`; its id drops `.Two` (`WoW2.…`), its
  namespaces root at `WoW.Two.Sdk.Beta.Be.Harvest`, and its row joins `engineering/architecture/package-registry.md`.
- The contract (`Abstractions`) holds no DI registration, options binding, logic or I/O; a new broker member gets a
  conformance case in `packages/Testing` that every implementation passes, the fake included.
- Store: PostgreSQL 16+ through `DatabaseSettings:ConnectionString` (`DB_CONNECTION` wins); the database must be UTF-8
  and offer `pg_trgm`, or boot refuses it. Tables carry the `harvest_` prefix, snake_case columns and enum text that
  the SQL CHECKs name; the module keeps its own keyed Npgsql data source and its migration set `harvest`.
- A new table is a new `packages/Module/Migrations/NNN-name/{Apply,Rollback}.sql` pair; never edit an applied one.
  The EF context maps over the SQL and never creates it.
- A lane works in its `packages/Module/{Lane}/` folder and maps the tables its lead doc names; a schema change another
  lane needs is a new migration, agreed in the contract first.
- `dotnet test Harvest.BackendServices.slnx` runs every tier; the database tiers need Docker, and the backend build
  needs Node and pnpm for the pages.
- A local run sets its connection string and the operator key (`Harvest:Operator:ApiKey`, `hv_` + 32 letters and
  digits) in `service/Harvest.Api/appsettings.Local.json`, which never leaves the machine.
