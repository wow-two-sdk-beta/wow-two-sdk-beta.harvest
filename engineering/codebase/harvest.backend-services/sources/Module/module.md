# HarvestAdapters.Module

Source composition over Harvest.Module: Afisha, ANONS, CAEx, Craftfair/Hunarmand, Uzexpocentre, the registry's three
generic routes (`generic-feed`, `generic-structured`, `generic-site`, over the generic readers), a capture cadence per
profile (cron `0 1,7,13,19 * * *` in `Asia/Tashkent` unless a profile names its own), the
`ocharo-fashion-v1` delivery filter (rules), the `ocharo-relevance-v1` filter (rules plus a `relevant` tag verdict at 0.5
or above for an event under `style-fashion`; `engineering/development/post-tags.md` § Classification), Ocharo's one
selector, `ocharo-selector.json` (`OcharoSelector`: the clauses it follows and the registry publishers whose posts count,
or `followRegistry` for every provisioned publisher, resolved to a container's source groups at setup) and explicit
repeatable source setup (`Ocharo/`); OLX and Domtut listings, the `marketplace-listing-v1` filter and repeatable marketplace profiles such as Haven's (`Marketplace/`,
`AddHarvestListingSources`, `MarketplaceProvisioner`).

Not packable: `HarvestSources.Api` references this project and the image ships it.

```csharp
// After the Harvest engine, readers, posts and delivery registrations:
services.AddOcharoEventSources();
// Explicit operator operation, through a scope:
await provisioner.EnsureAsync(enableSchedules: false);
// The same, plus the tenant's `ocharo-events` selector subscription (it follows every publisher the container provisions):
await provisioner.EnsureAsync(selector: true);
```

`selector: true` creates `ocharo-events` once and keeps it as it is afterwards, so a client's edits survive;
`selectorReset: true` puts the file's selector back as a selector change by `ocharo setup`, and a file equal to the stored
selector keeps the version. The per-pipeline `ocharo-fashion-v1` subscriptions keep being created beside it until they retire.

Uses Harvest's migrations and PostgreSQL. It declares no schema of its own or example capability.
