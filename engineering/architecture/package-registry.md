# wow-two-sdk-beta.harvest — Package registry

*Last updated: 2026-10-27*

> Every package this repo publishes, one version line per registry. Layer rules:
> wow-two-ws/conventions/development/repo/structure/domain-structure.md § Layers.

| Package | Layer | References | Heavy deps | Forms | Consumers |
|---|---|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Harvest.Abstractions` | contract | the kernel `WoW2.Sdk.Beta.Be.Abstractions` only; no framework reference | none | all | product Domain/Application projects; the private adapters repo |
| `WoW2.Sdk.Beta.Be.Harvest.Module` | forms | Abstractions, core (Npgsql, EF Core, SQL migrator, `Identity/ApiKeys`), AngleSharp, Cronos | HTML parser; no browser engine | 1 | products (Form 1), `service/Harvest.Api` |
| `WoW2.Sdk.Beta.Be.Harvest.Client` | forms | Abstractions, core | none | 2, 3 | Events, Haven, the ListingShelf view |
| `WoW2.Sdk.Beta.Be.Harvest.Testing` | test kit | Abstractions, `WoW2.Sdk.Beta.Be.Testing` | xunit (via be Testing) | all | test projects |
| `@wow-two-beta/fe-harvest` | fe | peers `@wow-two-beta/fe` `>=0.0.32 <0.1.0`, `vue` | none | all | `apps/web`, consoles and product admin pages |

- Core pin: `BackendSdkVersion` `10.0.81-beta` — the last core with `Identity/ApiKeys` and the first with named
  migration sets. Core `10.0.82` moved `Identity/ApiKeys` to the Identity repo, so the pin moves on once Harvest takes
  `WoW2.Sdk.Beta.Be.Identity.Auth` (same namespace); until then `core-latest.yml` runs red.
