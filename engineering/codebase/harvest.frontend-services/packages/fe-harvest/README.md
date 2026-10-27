# @wow-two-beta/fe-harvest

The Harvest domain's UI: the contract's TypeScript mirror, a client for the service's registry, and the management
screens a console composes.

- **Kit** — `createRegistryClient` reads containers, sources and pipelines from the consumer's own backend (Form 1, the
  default same-origin client) or the running Harvest service (Forms 2 and 3, `createRegistryClient({ baseUrl, apiKey })`).
- **Admin** — `ContainersPanel` lists the containers a key may see. The service's own console composes it; a central
  console or a product's admin page imports the same export.
- **Console** — `HarvestConsolePanel` manages sources, pipelines, runs and pull subscriptions and inspects posts,
  provenance and host health. Its optional `HarvestClient` works in every form. Without a client it offers an API-key
  connection held only in memory, and makes no authenticated requests until the user connects.

```bash
pnpm add @wow-two-beta/fe-harvest @wow-two-beta/fe vue
```

```ts
import { createRegistryClient, createHarvestClient } from '@wow-two-beta/fe-harvest/kit';
import { RegistrationRouteConstants, type PipelineModel } from '@wow-two-beta/fe-harvest/contracts';
import { ContainersPanel, HarvestConsolePanel } from '@wow-two-beta/fe-harvest/admin';
```

The panels are styled with Tailwind utilities on the fe sdk theme; add the package to the consumer's Tailwind sources:

```css
@source '../../node_modules/@wow-two-beta/fe-harvest/dist';
```

| Export | Holds |
|---|---|
| `./contracts` | `TenantModel`, `ApiKeyModel`, `ContainerModel`, `SourceModel`, `PipelineModel`, `ScheduleModel`, the enum labels, `RegistrationRouteConstants`, `HarvestScopeConstants`, `ApiKeyConstants` |
| `./kit` | `createRegistryClient`, `createHarvestClient` |
| `./admin` | `ContainersPanel`, `HarvestConsolePanel` — each accepts an optional client |

The contracts export run, host-health, post/link, subscription, delivery-event and cursor-page models too.
`HarvestConsolePanel` cancels stale reads on container changes and disconnect, polls every five seconds while visible,
and uses the shared fe SDK controls and theme. The service applies the `platform` chalk/forest ambient-glass theme.
