# @wow-two-beta/fe-harvest

The Harvest domain's UI: the contract's TypeScript mirror, a client for the service's registry, and the management
screens a console composes.

- **Kit** — `createRegistryClient` reads containers, sources and pipelines from the consumer's own backend (Form 1, the
  default same-origin client) or the running Harvest service (Forms 2 and 3, `createRegistryClient({ baseUrl, apiKey })`).
- **Admin** — the console's page panels; the service's own console composes them, and a central console or a
  product's admin page imports the same export.
- **Console** — `HarvestConsoleProvider` opens one console session (the connection, the open workspace and its data)
  and shares it with page panels in the operator's words — source, group, section, harvest, subscriber: `HomePanel`;
  `SourcesGallery` (groups as tiles, then each group's sources as cards or rows, with search and state filters,
  `SourceGroupsDialog` behind its Groups button); `SourcePanel` (a source's header card over its Overview, Posts,
  Activity and, for operators, Settings tabs; a post opens on its own page, `PostPanel`, with its images, facts and revisions);
  `SubscribersPanel`; `AccessPanel`. A section is turned on from the adapter's own section list, never a typed URL.
  `ContainerSwitch`, `ConsoleNotices` and `HarvestConnectPanel` fill the host's frame and signed-out screen. The
  provider's optional `HarvestClient` works in every form. Without a client it offers an API-key connection held only
  in memory, and makes no authenticated requests until the user connects. The host app owns routes and the frame; its
  `navigation` option resolves each `ConsolePlace` a panel links to.
- **Identity** — a host can supply a managed cookie client, sign-in path, granted permissions and account slot.
  `RegistryClientOptions` forwards the frontend SDK's `scope`, `onUnauthorized` and `antiforgery` options to both
  Harvest clients. The app composes the shared BFF strategy; the panels hold no client secret or OIDC token.

```bash
pnpm add @wow-two-beta/fe-harvest @wow-two-beta/fe vue
```

```ts
import { createRegistryClient, createHarvestClient } from '@wow-two-beta/fe-harvest/kit';
import { RegistrationRouteConstants, type SourceOverviewModel } from '@wow-two-beta/fe-harvest/contracts';
import { HarvestConsoleProvider, SourcePanel, SourcesGallery } from '@wow-two-beta/fe-harvest/admin';
```

The panels are styled with Tailwind utilities on the fe sdk theme; add the package to the consumer's Tailwind sources:

```css
@source '../../node_modules/@wow-two-beta/fe-harvest/dist';
```

| Export | Holds |
|---|---|
| `./contracts` | `TenantModel`, `ApiKeyModel`, `ContainerModel`, `SourceModel`, `PipelineModel`, `ScheduleModel`, the Sources area's `SourceOverviewModel`, `SourceGroupModel`, `SourcePostModel` and `PostRevisionModel`, the enum labels, `RegistrationRouteConstants`, `HarvestScopeConstants`, `ApiKeyConstants` |
| `./kit` | `createRegistryClient`, `createHarvestClient` |
| `./admin` | `HarvestConsoleProvider`, `useHarvestConsole` and the page panels it serves |

The contracts export run, host-health, post/link, subscription, delivery-event and cursor-page models too.
The console session cancels stale reads on workspace changes and disconnect, polls every five seconds while visible,
and the panels use the shared fe SDK controls and theme. The service applies the `platform` chalk/forest ambient-glass
theme.
