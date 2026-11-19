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
services.AddHarvestSources();
services.AddHarvestEngine();
services.AddHarvestWorker(); // omit this line when another host processes the queue

var app = builder.Build();
await app.Services.MigrateHarvestOnStartupAsync(); // create, check, migrate, check
```

- `IRegistrationBroker` — tenants, API keys, containers, sources and their groups, pipelines.
- `IRunBroker` — queue, inspect, cancel and pause work, pace a host; `HarvestEngine` exposes one scheduler/claim tick
  for hosts.
- `IPostBroker` — immutable captures, deduplication, field provenance, deletion checks and reviewed merges.
- `IDeliveryBroker` — pipeline subscriptions and resumable pull events with immutable revision payloads.
- `ISourceOverviewBroker` — the console's per-source overview, a source's posts and a post's revisions.
- `IPostTagsBroker` — the Layer 1 `post-tags` pass over the shared AI contract: `AddHarvestPostTags()` and
  `AddHarvestPostTagsWorker()`; post writes queue it only when `PostTagsOptions.Enabled` is set and the host composes
  the AI module. Its taxonomy is IAB Tech Lab Content Taxonomy 3.1 (CC BY 3.0).
- `IPostExtractionBroker` — Layer 2 extraction (`extract-event`, `extract-listing`) over the same AI contract:
  `AddHarvestPostExtraction()` and `AddHarvestPostExtractionWorker()`; done tags queue it only for the pairs
  `IPostExtractionDemand` wants (by default `PostExtractionOptions.Wanted`). It stores evidence-checked answers and
  never writes the post.
- `IEntityBroker` — the entity layer over the stored extraction answers, no model call: `AddHarvestEntities()` and
  `AddHarvestEntitiesWorker()`; a done answer queues resolution only when `EntityOptions.Enabled` is set. Each post binds
  with a role to one event (occurrences, cadence, a predicted next run) or one listing (offers across authors and
  sites); weak matches are flagged, never merged. Every entity change appends to the container's entity change feed.
- `IEntitiesBroker` — the entity reads: search with years, an entity's occurrences by year, its posts per year, the
  calendar and the change feed on a replaying pull cursor; `AddHarvestEntityReads()` (also added by
  `AddHarvestEntities()`), read only.
- Fetching, readers, extraction, posts, delivery and the engine register explicitly through their `Module.*` namespaces.
- Direct HTTP obeys host policy and reports refusals. No external transport, challenge solver or model provider is bound; the post-tags and extraction passes reference the AI contract only.
- `AddHarvestApiKeys()` — answers core's `ApiKey` scheme from the key table and the operator key, for a host that serves
  Harvest's HTTP API.
