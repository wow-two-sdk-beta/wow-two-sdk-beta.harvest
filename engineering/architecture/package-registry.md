# wow-two-sdk-beta.harvest — Package registry

*Last updated: 2026-11-12*

> Every package this repo publishes, one version line per registry, then the source readers that ship only inside the
> image. Layer rules: wow-two-ws/conventions/development/repo/structure/domain-structure.md § Layers.

| Package | Layer | References | Heavy deps | Forms | Consumers |
|---|---|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Harvest.Abstractions` | contract | the kernel `WoW2.Sdk.Beta.Be.Abstractions` only; no framework reference | none | all | product Domain/Application projects; `sources/*` |
| `WoW2.Sdk.Beta.Be.Harvest.Module` | forms | Abstractions, core (Npgsql, EF Core, SQL migrator), Identity.Auth, AngleSharp, Cronos | HTML parser; no browser engine | 1 | products (Form 1), `sources/Module`, both hosts |
| `WoW2.Sdk.Beta.Be.Harvest.Client` | forms | Abstractions, core, Identity.Auth | none | 2, 3 | Events, Haven, the ListingShelf view |
| `WoW2.Sdk.Beta.Be.Harvest.Hosting` | hosting | Module, core, Identity.Client, ASP.NET shared framework | optional durable BFF session cache; no source or provider dependency | 2, 3 | `service/HarvestSources.Api`, `service/Harvest.Api` |
| `WoW2.Sdk.Beta.Be.Harvest.Testing` | test kit | Abstractions, `WoW2.Sdk.Beta.Be.Testing` | xunit (via be Testing) | all | test projects |
| `@wow-two-beta/fe-harvest` | fe | peers `@wow-two-beta/fe` `>=0.0.41 <0.1.0`, `vue` | none | all | `apps/web`, consoles and product admin pages |

## Source readers

Not packable (`Directory.Build.props` by path: XML docs and warnings as errors, no package); project IDs keep the
`WoW.Two.Sdk.Beta.Be.HarvestAdapters.*` names and namespaces. `service/HarvestSources.Api` is their only consumer.

| Project | Responsibility | References |
|---|---|---|
| `HarvestAdapters.Afisha` | public category/detail discovery, embedded event schedules | Abstractions, AngleSharp |
| `HarvestAdapters.Anons` | event graph, Editor.js body, observed calendar URLs | Abstractions, AngleSharp |
| `HarvestAdapters.Caex` | bounded public venue calendar, date and organizer evidence | Abstractions, AngleSharp |
| `HarvestAdapters.Craftfair` | artisan events, application deadline/status evidence | Abstractions, AngleSharp |
| `HarvestAdapters.Uzexpocentre` | printed-year calendar rows and carried table cells | Abstractions, AngleSharp |
| `HarvestAdapters.Olx` | saved-filter catalogs, stable ad identities, listing fields and availability evidence | Abstractions, AngleSharp |
| `HarvestAdapters.Instagram` | official Business Discovery recent-media client and source broker | Abstractions, core |
| `HarvestAdapters.Integrations` | proxy/IP lease, supplied sessions, 2Captcha client | Abstractions, core |
| `HarvestAdapters.Module` | Ocharo registry, source composition, filtering and explicit setup; the marketplace | the source projects, Module, core |

## Pins

- Core pin: `BackendSdkVersion` `10.0.111-beta`; Identity `10.0.30-beta` supplies API-key auth and the BFF client.
- The Client takes client-credentials tokens through the be sdk's `Http.Auth`; `Harvest.Tests.Unit` uses Identity's
  `FakeTokenIssuer`.
- The console uses the frontend SDK's `AuthProvider`, BFF strategy, request scope and CSRF transport.
