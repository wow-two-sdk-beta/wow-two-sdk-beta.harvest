# wow-two-sdk-beta.harvest — Frontend Development Guidelines

Defer to `wow-two-ws/conventions/development/frontend/`.

## Workspace

- Frontend root: `engineering/codebase/harvest.frontend-services/`; Node `24.11.0` from `.nvmrc`, `pnpm@10.33.2`.
- `packages/fe-harvest` is `@wow-two-beta/fe-harvest`, published to npm: `./contracts` (the TypeScript mirror of the
  registry contract), `./kit` (`createRegistryClient`) and `./admin` (the console's provider and page panels). It peers `@wow-two-beta/fe` at `>=0.0.n <0.1.0`, `n` the pinned
  version; `check:package` enforces it and fails when one of the three exports is missing.
- `apps/web` is `@harvest/web`, private: the console, one route per page (Home; Sources and a source with its
  Overview, Posts, Processed, Pipelines, Activity and Settings tabs; Pipelines; Entities and one entity; Subscribers;
  Access) inside the fe sdk `AppFrame`. Posts shows the original post. Processed uses the Posts card's media proportions
  and puts the model's summary on the media. Processed cards show the tagged entity type, abbreviated post language
  and AI brand mark above the photo. Both galleries show the time since publication and first harvest below the
  title. Event dates and stated event languages sit in the extraction summary. The AI brand mark
  (`@wow-two-beta/fe/domain/aiBrands`) has a tooltip with the model, cost,
  latency, tokens and prompt version.
  The processed post page (`/sources/{id}/processed/{postId}`) centers the full photo and shows the original title,
  classification, tags, extraction evidence and AI passes. The Posts and Processed tab labels carry the source's totals
  (`Posts 88`) and a pill first in the filter row reads the match count (`12 of 88`) only while a search or filter
  narrows; hosted pages join as consumers forward to them. On screen it says source, group, section, harvest and subscriber, never pipeline, host
  lane, tenant, container or scope names. It takes
  `fe-harvest` by `workspace:*`, through its `exports` only — the way a product would; the package never imports
  `apps/`.
- The console counts a run as needing attention -- Home's failed count, Activity's "Needs attention" chip -- when it
  failed, was interrupted, or ended `partial` for a reason other than `page_limit` or `item_limit`; a run that stops
  at its own limit is a normal stop (`ConsoleFormat.isFailedRun`).
- App source uses `bootstrap`, `presentation`, `application`, `domain`, and `integration` layers.
- Routes live in `src/bootstrap/router/Routes.ts` on the fe sdk router (`createAppRouter`, `lazyPage`); a new hosted
  page is one route there, its path a constant in `fe-harvest/contracts`, its props decoded from the route.
- The kit is form-agnostic: it calls the consumer's origin (Form 1) or the service's (`baseUrl`), never both, with the
  Harvest API key in `X-Api-Key` when one is given.
- Management screens live in `fe-harvest/src/admin/`, exported as `./admin`, each taking an optional client; the console page composes them, and a central console or a product's admin page imports the same export.
  The console never builds a management screen of its own.

## Commands

Run from the frontend root; `dev`, `typecheck` and `test` build `fe-harvest` first, since `apps/web` reads its `dist/`.

```sh
pnpm install --frozen-lockfile
pnpm dev            # https on 8398, /api proxied to VITE_API_TARGET (the source host's https profile is 8400)
pnpm lint && pnpm test && pnpm build && pnpm check:package
```

`pnpm dev:http` or `VITE_HTTPS=false pnpm dev` skips local certificate setup; `VITE_API_TARGET` picks another backend.

## Delivery

`pnpm build` builds `fe-harvest` and the standalone app in `apps/web/dist/`. `publish-packages.yml` publishes
`fe-harvest` at the version its `fe-v*` tags and npm give.
