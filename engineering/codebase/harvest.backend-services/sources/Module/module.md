# HarvestAdapters.Module

Source composition over Harvest.Module: Afisha, ANONS, CAEx, Craftfair/Hunarmand, Uzexpocentre, the
`ocharo-fashion-v1` delivery filter (rules), the `ocharo-relevance-v1` filter (rules plus a `relevant` tag verdict at 0.5
or above for an event under `style-fashion`; `engineering/development/post-tags.md` § Classification) and explicit
repeatable source setup (`Ocharo/`); OLX and Domtut listings, the
`marketplace-listing-v1` filter and repeatable marketplace profiles such as Haven's (`Marketplace/`,
`AddHarvestListingSources`, `MarketplaceProvisioner`).

Not packable: `HarvestSources.Api` references this project and the image ships it.

```csharp
// After the Harvest engine, readers, posts and delivery registrations:
services.AddOcharoEventSources();
// Explicit operator operation, through a scope:
await provisioner.EnsureAsync(enableSchedules: false);
```

Uses Harvest's migrations and PostgreSQL. It declares no schema of its own or example capability.
