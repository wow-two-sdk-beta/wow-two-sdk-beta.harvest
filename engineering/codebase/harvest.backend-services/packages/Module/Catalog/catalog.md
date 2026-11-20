# Catalog/

The instance's source catalog: which registered sources (the Ocharo registry's publishers, the marketplace sites) this
instance turns on (`engineering/development/source-catalog.md`). The image ships every entry; the instance's database
records the choice, and an off source never schedules, captures, is processed by a model or delivers. Nothing is deleted.

| Implements (Abstractions) | Over |
|---|---|
| `Sources.ISourceCatalog` (declared by `sources/Module`) · `ISourceCatalogRecords` → `SourceCatalogStore` · `CatalogSourceModel` | `harvest_catalog_sources`, `harvest_sources.catalog_key` (migration `019-source-catalog`) |

- Table: `harvest_catalog_sources` (`key` pk, `turned_on`, `changed_at`, `changed_by`), one record per entry the instance decided
  on. No record and no stamped source is `new`; a stamped source with no record reads `on`.
- Gate fragment: `CatalogGate.SourceOff` (alias `s`) / `Off(alias)` is `EXISTS(… turned_on = false for the source's
  catalog_key)`; every gate reads it when it acts: `HarvestEngine` (candidates, claim), `RunPlanner.QueueAsync` and the resume in
  `ModuleRunBroker` (`source-turned-off`), `SaveCaptureAsync`, `PostTagsStore.AutoSql`/`ModeSql`, `DeliveryEventWriter.AppendOneAsync`
  (`PostOff`: every link an off source) and the section reconcile. `SourceModel.TurnedOn` carries it; the overview omits off sources.
- `SourceCatalogBroker` lists entries with their record and stamped sources, and switches one: on sets a `new` entry up first
  (schedules and model processing off; a failure is a `Conflict` and the record stays), off writes the record. A switch takes the
  scheduler lock the master pause takes.
- Boot: `ReconcileHarvestSourceCatalogAsync` stamps the sources each catalog finds and writes `on` / `existing` for stamped keys with
  no record; an existing record never changes, and a rerun changes nothing.
