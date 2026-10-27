# WoW2.Sdk.Beta.Be.Harvest.Module

Form 1 of Harvest: the contract's brokers in the consumer's process, over the consumer's PostgreSQL database. The
database must be UTF-8 and offer `pg_trgm`; boot refuses it otherwise. The module keeps its own Npgsql data source
(keyed `harvest`) and its own migration set, so its history (`harvest_migration_history`) and its `harvest_` tables sit
beside the consumer's without touching them.

```bash
dotnet add package WoW2.Sdk.Beta.Be.Harvest.Module --prerelease
```

```csharp
services.AddHarvest().Configure(options => options.ConnectionString = "Host=…;Database=app;…");

var app = builder.Build();
await app.Services.MigrateHarvestOnStartupAsync(); // create, check, migrate, check
```

- `IRegistrationBroker` — tenants, API keys, containers, sources, pipelines.
- `AddHarvestApiKeys()` — answers core's `ApiKey` scheme from the key table and the operator key, for a host that serves
  Harvest's HTTP API.
