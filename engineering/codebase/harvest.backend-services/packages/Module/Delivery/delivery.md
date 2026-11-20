# Delivery/

`IDeliveryBroker` registers a tenant's channel or selector subscription and reads its durable event log.
`AddHarvestDelivery()` registers the PostgreSQL implementation. Webhook delivery remains outside v0.1.

| Implements (Abstractions) | How |
|---|---|
| `Delivery.SubscriptionModel` · `DeliveryEvent` · `DeliveryEventTypes` · `CursorPage<T>`, and the subscription broker this lane declares | one durable log per subscription in `harvest_subscription_event`; the pull cursor reads `(tx, id)` below `pg_snapshot_xmin(pg_current_snapshot())` |

- Owns: `harvest_subscriptions`, `harvest_subscription_event`, `harvest_subscription_selectors`.
- Rules: rows hold references and the immutable post revision snapshot, payloads render at read time; at least once;
  `event_id` stable across retries; `item_id` stable across subscriptions; webhook push waits on the core durable
  dispatcher, pull ships first.
- a subscription can access the shared pool or its tenant's own container; its pipeline must belong to that container
- live/since backfill and capture writes share the container transaction lock, so joining cannot miss a racing write
- hosts register `IDeliveryPostFilter` implementations; nullable `FilterKey` selects one independently of opaque
  `Context`. Null accepts all posts; unknown keys fail subscription validation
- capture and backfill snapshots apply the filter; absent or throwing filters withhold new snapshots while raw
  posts and captures remain available. Stored filter keys survive restarts
- known posts retain lifecycle events after a filter stops matching; new post identities always require a match
- a known post's first revision that fails its filter or section goes out as `post.unmatched`; later failing
  revisions, a `post.changed` that restores a gone post included, wait until one passes and goes out as its own type
  (`DeliveryEventWriter.Deliverable`). An absent or throwing filter decides nothing: no `post.unmatched`, and
  rollup-state changes of a post not yet unmatched still go out
- a pull cursor binds its subscription and reads `(tx, id)` below `pg_snapshot_xmin(pg_current_snapshot())`;
  transactions that commit late cannot be skipped, and long-running transactions can delay visibility
- an empty poll preserves its cursor; an older cursor replays the same event identifiers and revision snapshots
- `wow2.event.v1` subscriptions project the stored event mapping with post identity, revision, state, links, field
  provenance and `capture` (the newest captured link's id, time and URL); a subscription without that schema reads the
  complete post snapshot
- `media[].fileRef` of a `wow2.event.v1` payload is the Files file id (GUID, `D` form) of the copy SCFD holds for that image,
  filled when a page is pulled (`DeliveryMediaFiles`) from `harvest_source_assets` through the sources the post is linked
  from; the source `url` stays beside it as the credit link, and an image with no copy yet keeps `null`. A copy made after
  an event was pulled reaches the subscriber with the post's next event
- every pulled event names its subscription's `pipelineId`; tag, keyword and selector subscriptions leave it null
- a stored event projection that carries the `mapping` stamp renders again through the current `EventSchemaMapper`, so
  every revision reads with camelCase string enums; a reader's own projection, without the stamp, is sent as stored
- a filter whose `IDeliveryPostFilter.IgnoresTagOnlyChange` is true (`marketplace-listing-v1`) goes without the `post.changed` of a
  revision that differs from the subscription's newest received snapshot only in `tags`, `classification`, `extras.postTags`
  and `extras.listingAi` (`TagOnlyChange`): whatever wrote the tags, the post's revision rises and nothing is delivered. A
  changed title, text, price, date, media or state still is; selector subscriptions and other filters are untouched
- a done `extract-listing` answer is delivered as `post.extracted` (`DeliveryEventWriter.AppendExtractedAsync`), written by the
  Posts lane in the answer's transaction: to each active channel subscription that already holds the post (an earlier
  `post.new`, `post.changed` or `post.extracted`), inside its section and passing its filter, and to a selector subscription
  that follows it, with `extras.listingAi` on the delivered post (`contract.md` § Listing delivery). The item id adds
  `:ai{promptVersion}:{model}`, so a replay of the same answer is a no-op and another prompt version or model is its own
  event; the reason is `ingest`. The stored post never holds the extras, and no revision is raised
- `ApiDeliveryBroker` and `ApiPostBroker` share `AddHarvestDataClient()`; `PostDeliveryBrokerConformance` runs the same
  contract cases against the module, HTTP client and in-memory consumer fakes

### Selector subscriptions

A selector subscription (`kind='selector'`, with `name`, `selector jsonb` and `selector_version`, migration
`018-subscription-selectors`) follows Layer 1 tags and sources over one container instead of one pipeline.
`SelectorStore` reads and writes what it needs; the rules live in `engineering/architecture/contract.md` § Subscription
selectors.

- Every write: `DeliveryEventWriter.AppendAsync` runs the channel loop, then judges the revision against each active
  selector subscription of the container with `SubscriptionSelectorMatcher.Evaluate` and `Choose`, so a write reaches a
  selector whatever run caused it. The verdict, the selector version, the clauses and the merged ref context are stored in
  `harvest_subscription_event.matched`; a pending post (no tags yet) writes nothing until the tags' own `post.changed`.
- Subscribe: a declaration with `selector` needs a free `name` (unique per tenant among non-archived subscriptions) and
  refuses `pipelineId` and `filterKey`; the selector is normalized, then validated by `SelectorStore.ValidateAsync`
  (`SelectorRules.Validate`, then every domain, `except` and `excludeDomains` key an active taxonomy node, and every ref
  a group, source or pipeline of the container). Backfill `Live` or `Since` appends `post.new` (`backfill`) for each live
  match under the container lock, and version 1 enters `harvest_subscription_selectors`.
- Change: `SetSelectorAsync` takes the container lock, writes nothing for an equal selector, and otherwise raises the
  version and re-judges one window on the same cursor: every post the log has an event of, plus live posts changed since
  the window (30 days by default, at most 365 days and 10,000 posts, else refused whole). Its item ids carry `:s{version}`,
  and the version row keeps the window, the counts and `changed_by`.
- State: `SetStateAsync` swaps `active` and `suspended`, takes any state to `archived`, and refuses a way out of
  `archived`; only an active subscription receives writes, and an archived one frees its name.
- Read: `PullAsync` renders `DeliveryEvent.Match` from `matched` and sets the payload's top-level `context` to its stored
  context, after the schema's render; a registered `ISelectorChangeHook` runs after each committed subscribe or change
  with the subscription and the window, and a hook that throws is logged and ignored.
