# wow-two-sdk-beta.harvest — Package registry

*Last updated: 2026-10-27*

> Every package this repo publishes, one version line per registry. Layer rules:
> wow-two-ws/conventions/development/repo/structure/domain-structure.md § Layers.

| Package | Layer | References | Heavy deps | Forms | Consumers |
|---|---|---|---|---|---|
| `WoW2.Sdk.Beta.Be.Harvest.Abstractions` | contract | the kernel `WoW2.Sdk.Beta.Be.Abstractions` only; no framework reference | none | all | {{product Domain/Application projects}} |
| `WoW2.Sdk.Beta.Be.Harvest.Module` | forms | Abstractions, core | none | 1 | {{products}}, `service/Harvest.Api` |
| `WoW2.Sdk.Beta.Be.Harvest.Client` | forms | Abstractions, core | none | 2, 3 | {{products}} |
| `WoW2.Sdk.Beta.Be.Harvest.Testing` | test kit | Abstractions, `WoW2.Sdk.Beta.Be.Testing` | xunit (via be Testing) | all | test projects |
| `@wow-two-beta/fe-harvest` | fe | peers `@wow-two-beta/fe` `>=0.0.n <0.1.0`, `vue` | none | all | {{frontends}}, `apps/web` |
