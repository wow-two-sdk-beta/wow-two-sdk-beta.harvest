# @wow-two-beta/fe-harvest

The Harvest domain's UI: the contract's TypeScript mirror, a client for the service's registry, and the management
screens a console composes.

- **Kit** — `createRegistryClient` reads containers, sources and pipelines from the consumer's own backend (Form 1, the
  default same-origin client) or the running Harvest service (Forms 2 and 3, `createRegistryClient({ baseUrl, apiKey })`).
- **Admin** — `ContainersPanel` lists the containers a key may see. The service's own console composes it; a central
  console or a product's admin page imports the same export.

```bash
pnpm add @wow-two-beta/fe-harvest @wow-two-beta/fe vue
```

```ts
import { createRegistryClient } from '@wow-two-beta/fe-harvest/kit';
import { RegistrationRouteConstants, type PipelineModel } from '@wow-two-beta/fe-harvest/contracts';
import { ContainersPanel } from '@wow-two-beta/fe-harvest/admin';
```

The panels are styled with Tailwind utilities on the fe sdk theme; add the package to the consumer's Tailwind sources:

```css
@source '../../node_modules/@wow-two-beta/fe-harvest/dist';
```

| Export | Holds |
|---|---|
| `./contracts` | `TenantModel`, `ApiKeyModel`, `ContainerModel`, `SourceModel`, `PipelineModel`, `ScheduleModel`, the enum labels, `RegistrationRouteConstants`, `HarvestScopeConstants`, `ApiKeyConstants` |
| `./kit` | `createRegistryClient` |
| `./admin` | `ContainersPanel` — takes an optional `client`, like every management screen |
