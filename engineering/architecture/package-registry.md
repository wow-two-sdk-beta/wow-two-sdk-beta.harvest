# wow-two-sdk-beta.harvest — Package registry

*Last updated: 2026-11-03*

> Every package this repo publishes, one version line per registry. Layer rules:
> wow-two-ws/conventions/development/repo/structure/domain-structure.md § Layers.

| Package | Layer | References | Heavy deps | Forms | Consumers |
|---|---|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Harvest.Abstractions` | contract | the kernel `WoW2.Sdk.Beta.Be.Abstractions` only; no framework reference | none | all | product Domain/Application projects; the private adapters repo |
| `WoW2.Sdk.Beta.Be.Harvest.Module` | forms | Abstractions, core (Npgsql, EF Core, SQL migrator), Identity.Auth, AngleSharp, Cronos | HTML parser; no browser engine | 1 | products (Form 1), `service/Harvest.Api` |
| `WoW2.Sdk.Beta.Be.Harvest.Client` | forms | Abstractions, core, Identity.Auth | none | 2, 3 | Events, Haven, the ListingShelf view |
| `WoW2.Sdk.Beta.Be.Harvest.Hosting` | hosting | Module, core, Identity.Client, ASP.NET shared framework | optional durable BFF session cache; no private source/provider dependency | 2, 3 | generic service; private adapter hosts |
| `WoW2.Sdk.Beta.Be.Harvest.Testing` | test kit | Abstractions, `WoW2.Sdk.Beta.Be.Testing` | xunit (via be Testing) | all | test projects |
| `@wow-two-beta/fe-harvest` | fe | peers `@wow-two-beta/fe` `>=0.0.41 <0.1.0`, `vue` | none | all | `apps/web`, consoles and product admin pages |

- Core pin: `BackendSdkVersion` `10.0.111-beta`; Identity `10.0.30-beta` supplies API-key auth and the BFF client.
- The Client takes client-credentials tokens through the be sdk's `Http.Auth`; `Harvest.Tests.Unit` uses Identity's
  `FakeTokenIssuer`.
- The console uses the frontend SDK's `AuthProvider`, BFF strategy, request scope and CSRF transport.
