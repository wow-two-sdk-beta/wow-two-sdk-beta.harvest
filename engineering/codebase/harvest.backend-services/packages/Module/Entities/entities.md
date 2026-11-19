# Entities/

The entity layer (wow-two-ws `ideas/scfd-entity-model.md` build plan step C): every extracted post binds, with a role,
to the one real-world event or listing it is about. An event is one record across its editions, with a cadence and a
predicted next run; a listing is one record across authors and sites, each ad an offer. Dedup is the binding. This lane
never writes the post and never calls a model.

| Implements | Over |
|---|---|
| `IEntityBroker` (resolve, backfill, show, search, merge, split, report, the worker's tick) | the PostExtraction lane's done answers (`harvest_post_extractions`) and answer types; the Tagging lane's done tags and content hash; the Posts lane's links and sources |

- Owns: `harvest_entities`, `harvest_entity_occurrences`, `harvest_entity_relations`, `harvest_entity_keep_apart`,
  `harvest_entity_flags` and the `entity-resolve` kind of `harvest_post_work` (014).
- Queue: `PostExtractionStore.WriteAsync` queues `entity-resolve` for the answer's content in its own transaction when
  `Harvest:Entities:Enabled` is set (`EntityGate`); `entities backfill` queues answered posts with neither a relation
  nor a flag. The worker re-checks the post and its hash, resolves under the container's advisory lock
  (`entities:{container}`) and drops a row whose post moved on.
- Names (`EntityNames`): `name_key` is the sorted Latin words of a name, Cyrillic transliterated to Uzbek Latin,
  apostrophes dropped, years, numbers, ordinals and stopwords removed, and a small glossary of event and place words
  mapped to English. `aliases` keep the sources' names; `alias_keys` their keys. Places compare by `PlaceKey` stems.
- Events (`EntityResolver`): candidates are the container's events whose `name_key` or an alias key reaches the floor in
  pg_trgm similarity; a known different city or tier-1 domain rules one out; the same city and an edition overlap
  (dates within `EventDateWindowDays`, or the edition label's year) add 0.1 each. Roles: `announcement` creates or
  confirms occurrences (a predicted one turns real), `update` applies `changes` (dates, a `cancelled` or `postponed`
  status, place, prices, registration) with history, `report` marks the occurrence held, `mention` and `unsure` only
  attach. An estimated date never overrides a stated one.
- Cadence (`EntityCadence`): from two or more stated starts; one `predicted` occurrence after the last real one while
  the cadence's confidence reaches `Cadence:PredictMinConfidence`, replaced when a real run arrives.
- Listings: the same post or the same `(source_id, external_key)` is an `update` on its listing; otherwise
  `ListingIdentity` scores item identity, the same author (a hash of the post's author url, handle or name on its
  source) adding 0.2. The same author's ad is an `update`, another author's an `announcement`; each relation keeps its
  own `offer` (author kind and key, price, link, source, external key, first seen), and `facts.priceSpread` keeps each
  author's latest price per currency and period.
- Unsure (#9): a best candidate under `MinMatchConfidence`, or two within `AmbiguityMargin`, binds nothing and upserts a
  `harvest_entity_flags` row (`match`, `weak-match` or `ambiguous`, the candidate and its confidence); a name with no
  key flags `name` / `no-name`. A later binding of the post closes its flags.
- Corrections: `merge` folds one entity into another of the same type (relations, occurrences of the same edition,
  aliases, facts, keep-apart pairs) and marks it `merged_into`; `split` records a keep-apart pair, which a merge then
  refuses, and resolution never binds a post to an entity kept apart from the one it is already bound to.
- Facts keep no evidence quotes and no post text; the verbs print none.
