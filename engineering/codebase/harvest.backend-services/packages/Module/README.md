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
services.AddHarvestFetching();
services.AddHarvestReaders();
services.AddHarvestExtraction();
services.AddHarvestPosts();
services.AddHarvestDelivery();
services.AddHarvestEngine();
services.AddHarvestWorker(); // omit this line when another host processes the queue

var app = builder.Build();
await app.Services.MigrateHarvestOnStartupAsync(); // create, check, migrate, check
```

- `IRegistrationBroker` — tenants, API keys, containers, sources, pipelines.
- `IRunBroker` — queue, inspect, cancel and pause work; `HarvestEngine` exposes one scheduler/claim tick for hosts.
- `IPostBroker` — immutable captures, deduplication, field provenance, deletion checks and reviewed merges.
- `IDeliveryBroker` — pipeline subscriptions and resumable pull events with immutable revision payloads.
- Fetching, readers, extraction, posts, delivery and the engine register explicitly through their `Module.*` namespaces.
- Direct HTTP obeys host policy and reports refusals. No external transport, challenge solver or model provider is bound.
- `AddHarvestApiKeys()` — answers core's `ApiKey` scheme from the key table and the operator key, for a host that serves
  Harvest's HTTP API.
