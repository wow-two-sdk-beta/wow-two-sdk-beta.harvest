# Delivery/

`IDeliveryBroker` registers a tenant's channel subscription and reads its durable event log.
`AddHarvestDelivery()` registers the PostgreSQL implementation. Webhook delivery remains outside v0.1.

| Implements (Abstractions) | How |
|---|---|
| `Delivery.SubscriptionModel` · `DeliveryEvent` · `DeliveryEventTypes` · `CursorPage<T>`, and the subscription broker this lane declares | one durable log per subscription in `harvest_subscription_event`; the pull cursor reads `(tx, id)` below `pg_snapshot_xmin(pg_current_snapshot())` |

- Owns: `harvest_subscriptions`, `harvest_subscription_event`.
- Rules: rows hold references and the immutable post revision snapshot, payloads render at read time; at least once;
  `event_id` stable across retries; `item_id` stable across subscriptions; webhook push waits on the core durable
  dispatcher, pull ships first.
- a subscription can access the shared pool or its tenant's own container; its pipeline must belong to that container
- live/since backfill and capture writes share the container transaction lock, so joining cannot miss a racing write
- a pull cursor binds its subscription and reads `(tx, id)` below `pg_snapshot_xmin(pg_current_snapshot())`;
  transactions that commit late cannot be skipped, and long-running transactions can delay visibility
- an empty poll preserves its cursor; an older cursor replays the same event identifiers and revision snapshots
- `wow2.event.v1` subscriptions project the stored event mapping with post identity, revision, state, links and field
  provenance; a subscription without that schema reads the complete post snapshot
- `ApiDeliveryBroker` and `ApiPostBroker` share `AddHarvestDataClient()`; `PostDeliveryBrokerConformance` runs the same
  contract cases against the module, HTTP client and in-memory consumer fakes
