# Delivery/

Subscriptions and the event log — iteration 7.

| Implements (Abstractions) | How |
|---|---|
| `Delivery.SubscriptionModel` · `DeliveryEvent` · `DeliveryEventTypes` · `CursorPage<T>`, and the subscription broker this lane declares | one durable log per subscription in `harvest_subscription_event`; the pull cursor reads `(tx, id)` below `pg_snapshot_xmin(pg_current_snapshot())` |

- Owns: `harvest_subscriptions`, `harvest_subscription_event`.
- Rules: rows hold references, payloads render at read or send time; at least once, per-post FIFO for push;
  `event_id` stable across retries; `item_id` stable across subscriptions; webhook push waits on the core durable
  dispatcher, pull ships first.
