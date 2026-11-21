# Source catalog

*Last updated: 2026-11-21*

Each SCFD instance chooses which registered sources it ever uses. The image ships every registered source (the Ocharo
registry, the marketplace sites); the instance's own database records which of them are turned on. The console's
Sources page lists only turned-on sources, and Available lists the rest. Turning a source off disables it on that
instance: its rows, posts, captures, runs and settings stay, and it can be turned on again.

Code: the seam `ISourceCatalog` in `packages/Abstractions/Sources/`; the record, the gate fragment and the broker in
`packages/Module/Catalog/` (lead doc `catalog.md`); the routes in `packages/Hosting/Sources/`; the catalogs in
`sources/Module/Ocharo/` and `sources/Module/Marketplace/`; the `sources catalog` verb in
`service/HarvestSources.Api/Sources/`. Developer, 2026-11-21: "instance itself will be able to configure which sources
will be ever turned on - saved in db"; "we can remove afisha etc from this one - like disable not completely remove".

## Entries

| Catalog | Entry | Key | 2026-11-21 |
|---|---|---|---|
| Ocharo registry (`ocharo-event-sources.json`) | a publisher group with at least one channel in `mode` `harvest` or `provider` | `ocharo.{groupId}`, e.g. `ocharo.afisha` | 57 entries |
| Marketplace (`MarketplaceProfile` sites) | a site | `marketplace.{siteKey}`, e.g. `marketplace.olx-uz` | 4 entries (BirBir's import and collection sites share `birbir-uz`) |

- An entry is the unit the console and the developer name: a publisher (Afisha, ANONS) or a site (OLX, BirBir). Its
  channels or searches turn on and off with it; a channel's own choice stays with its pipeline (sections, pause).
- A registry wave adds entries by editing the data file; a new entry starts `new` on every instance, so a wave never
  starts collecting anywhere by itself.
- A group with no collectible channel (`planned`, `reference`, `manual`) is no entry, since nothing of it could run.
- A source row links to its entry through `harvest_sources.catalog_key`. A source with no key, registered by hand through
  `POST api/sources`, stays outside the catalog and is listed and run as before.

## States

| State | Record | Sources page | Available | Runs, captures, AI, delivery |
|---|---|---|---|---|
| `new` | none, and no source set up | — | listed | nothing exists to run |
| `on` | `turned_on = true`, or none with a source set up | listed | — | as the source's own settings say |
| `off` | `turned_on = false` | hidden | listed, `Turned off {date}` | none |

## Record

Migration `019-source-catalog` (expand-only, lock-safe, as the migration lint requires):

- `harvest_catalog_sources`: `key text primary key`, `turned_on boolean not null`, `changed_at timestamptz not null`,
  `changed_by text not null` (an Identity subject, `operator-key`, `setup` or `existing`)
- `harvest_sources.catalog_key text null`, indexed
- the rollback drops both

The record belongs to the instance, not to a tenant or container: Haven's instance and Ocharo's `harvest-dev` each keep
their own in their own database, and both list every registered entry.

## What an off source never does

A source is off when `harvest_catalog_sources` holds `turned_on = false` for its `catalog_key`; one SQL fragment states
it, and every gate reads it when it acts, not when the work was queued.

| Work | Gate | An off source |
|---|---|---|
| Scheduled runs | the scheduler's candidate query (`HarvestEngine.QueueDueAsync`) | is never a candidate |
| Manual runs, setup `--run`, run resume | `RunPlanner.QueueAsync`, `ModuleRunBroker` resume | refused with `source-turned-off` |
| Capture | the work claim (`HarvestEngine.ClaimAsync`) | leaves its queued work queued and unclaimed |
| Capture writes and imports | `ModulePostBroker.SaveCaptureAsync` | refused |
| AI: tags, extraction, entities, re-runs, the Process stage | the model-processing fragments (`PostTagsStore.AutoSql`, `ModeSql`), re-checked at claim | reads as `off`: queues nothing, and drops queued work at claim as mode `off` does; `processing rerun` restores it |
| Image copies | the source-asset worker (`SourceAssetStore`) | leaves its queued copies queued and unfetched |
| Delivery | the event writes and the selector scope | writes no event for a post whose every link is an off source |
| Section staging at boot | `ReconcileHarvestSourceSectionsAsync` | is skipped |

- Turning off takes the scheduler lock the master pause takes; a step already in flight may finish.
- Turning on again resumes the source as it was: neither switch changes its pause, schedules, sections or
  model-processing mode.
- Nothing is deleted: rows, posts, captures, runs, subscriptions and their logs stay; events delivered before stay
  delivered, and turning off emits no `post.unmatched`.
- Eval runs over stored posts still run: they score a prompt and change no post and deliver nothing.

## Defaults

- Existing sources stay on: at boot, each catalog stamps `catalog_key` on the sources it set up, matched by family and
  handle in its container, and writes `on` (`changed_by` `existing`) for each stamped key with no record. An existing
  record never changes, and a rerun changes nothing.
- On `harvest-dev`, Afisha, ANONS, CAEx, Uzexpocentre and Haven's listing sites therefore stay on.
- A fresh instance starts with every entry `new`.

## Who switches

- Platform operators only: `Harvest:Console:OperatorSubjectIds`, the operator API key and operator service clients.
  The switch needs the `ManageSources` policy; the catalog read needs the operator too, since it lists the whole
  registry, not one tenant's sources.
- A tenant caller sees neither the catalog nor Available; its Sources page leaves off sources out as well.

## API

| Route | Policy | Does |
|---|---|---|
| `GET api/sources/catalog` | operator | lists every entry: key, catalog, name, hosts, channel count, state, changed at and by, and the sources it set up (id, container, name) |
| `PUT api/sources/catalog/{key}` `{ "turnedOn": true }` | `ManageSources` | turns the entry on; a `new` entry is [set up first](#setting-up-on-turn-on) |
| `PUT api/sources/catalog/{key}` `{ "turnedOn": false }` | `ManageSources` | turns the entry off; a `new` entry gets an off record and nothing is set up |
| `GET api/sources/overview` | as before | leaves off sources out |
| `GET api/sources`, `GET api/sources/{id}` | as before | carry `catalogKey` and `turnedOn`; an off source still answers, so its page opens from Available |

- An unknown key answers 404. A set-up that fails answers 409 with its reason, and the record stays as it was.
- The verb `sources catalog [--state new|on|off]`, `sources catalog on <key>` and `sources catalog off <key>` does the
  same reads and switches for Wheelhouse Run, one JSON line each.

## Setting up on turn on

- A `new` entry turned on is set up through its catalog's provisioner with schedules off and model processing `off`,
  the defaults `ocharo setup` and `marketplace setup` use without `--schedules`.
- An Ocharo group lands in its tenant's `ocharo-events` container (tenant `ocharo`); a marketplace site lands in each
  profile that lists it, with that profile's searches on the site as pipelines.
- The source then reads Manual on Sources until its schedules are switched on in its Settings, or a setup with
  `--schedules` runs.
- An `off` entry turned on resumes as it was; one turned off before anything was set up is set up as a `new` one.

## Setup verbs

- `ocharo setup` and `marketplace setup` stamp `catalog_key` and turn on every entry they set up.
- A bulk setup (`ocharo setup` with no `--group`, `marketplace setup --profile`) skips an `off` entry and lists it under
  `skipped`, so it never turns an operator's deliberate off back on.
- `ocharo setup --group <id>` names the entry, so it turns that group on even when it was off.
- `POST api/ocharo/sources/setup` and `POST api/marketplace/{profile}/setup` follow the same rules.

## Console

- Collect › Sources lists the turned-on sources of the chosen container; the sidebar count counts only them.
- Collect › Available, for operators only, lists every `new` and `off` entry with search and the Cards/Rows switch:
  name, catalog (Events or Listings), hosts and channels; an off entry adds `Turned off {date}` and links to its
  sources. Each card has **Turn on**; its count sits in the sidebar.
- A catalog source's Settings tab reads "Turned on for this instance" with **Turn off**. A confirm names the entry and
  every source it covers, and says what stops and what stays; the console then opens Available.
- The Sources gallery's "Available only" toggle becomes "Active only": it hides paused and unavailable sources, which
  is a different question.
- The demo client seeds `new` and `off` entries, so Available renders in `pnpm dev:demo`.

## Limits

- Two groups claiming one host would share one source row and the first group's key; the registry has none
  (each Instagram account and each Telegram channel is its own source).
- A source's favicon copy still fetches while it is off: one request per host, outside the posts.
- The catalog has no `packages/Client` broker: the console reads it over HTTP, and no product embeds it yet.
