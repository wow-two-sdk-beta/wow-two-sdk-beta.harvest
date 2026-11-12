# HarvestAdapters.Module

Source composition over Harvest.Module: Afisha, ANONS, CAEx, Craftfair/Hunarmand, Uzexpocentre, the
`ocharo-fashion-v1` delivery filter and explicit repeatable source setup (`Ocharo/`); OLX listings
(`Marketplace/`, `AddHarvestListingSources`).

Not packable: `HarvestSources.Api` references this project and the image ships it.

```csharp
// After the Harvest engine, readers, posts and delivery registrations:
services.AddOcharoEventSources();
// Explicit operator operation, through a scope:
await provisioner.EnsureAsync(enableSchedules: false);
```

Uses Harvest's migrations and PostgreSQL. It declares no schema of its own or example capability.
