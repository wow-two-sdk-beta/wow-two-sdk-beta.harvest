import type { RouteLocationNormalizedLoaded } from 'vue-router';
import { createAppRouter, lazyPage, lazyRoute, RouterHistoryMode, type RouteConfig } from '@wow-two-beta/fe/router';
import type { SourceTab } from '@wow-two-beta/fe-harvest/admin';
import ConsoleSession from '../ConsoleSession.vue';

/** The group a `#group-{id}` hash names, or nothing. @internal */
function groupOfHash(route: RouteLocationNormalizedLoaded) {
  const match = /^#group-(.+)$/.exec(route.hash);
  return match ? decodeURIComponent(match[1]!) : undefined;
}

/** One source's page on a tab: its own path, its props from the route. @internal */
function sourceTab(path: string, id: string, title: string, tab: SourceTab) {
  return {
    path,
    id,
    handle: { title },
    lazy: lazyPage(
      () => import('../../presentation/console/pages/SourcePage.vue'),
      (route) => ({ sourceId: String(route.params['id'] ?? ''), tab }),
    ),
  };
}

/** The pages iteration 1 removed; their old addresses open Sources. @internal */
const RemovedPages = ['pipelines', 'pipelines/:id', 'runs', 'posts', 'subscriptions', 'hosts'];

/**
 * Holds the route table: one console layout route, and a page per place in the operator's flow — Home; Sources and one
 * source with its tabs (`/sources/:id`, `/posts`, `/activity`, `/settings`) and one of its posts
 * (`/sources/:id/posts/:postId`); Subscribers; Access. Each title matches its
 * sidebar label. The Pipelines, Runs, Hosts, Posts and Subscriptions pages are gone; their paths redirect to Sources.
 */
export const routes: RouteConfig = [
  {
    path: '/',
    id: 'console',
    component: ConsoleSession,
    children: [
      {
        index: true,
        id: 'home',
        handle: { title: 'Home', description: 'How the open workspace is doing: harvests, failures and delivery.' },
        lazy: lazyRoute(() => import('../../presentation/console/pages/HomePage.vue')),
      },
      {
        path: 'sources',
        id: 'sources',
        handle: {
          title: 'Sources',
          description: 'The sources we can read, by publisher. Open one for its sections and posts.',
        },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/SourcesPage.vue'),
          (route) => ({ focusGroup: groupOfHash(route) }),
        ),
      },
      sourceTab('sources/:id', 'source', 'Source', 'overview'),
      sourceTab('sources/:id/posts', 'source-posts', 'Posts', 'posts'),
      {
        path: 'sources/:id/posts/:postId',
        id: 'source-post',
        handle: { title: 'Post' },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/PostPage.vue'),
          (route) => ({ sourceId: String(route.params['id'] ?? ''), postId: String(route.params['postId'] ?? '') }),
        ),
      },
      sourceTab('sources/:id/activity', 'source-activity', 'Activity', 'activity'),
      sourceTab('sources/:id/settings', 'source-settings', 'Settings', 'settings'),
      {
        path: 'subscribers',
        id: 'subscribers',
        handle: {
          title: 'Subscribers',
          description: 'The products that receive posts, and the source and section each one follows.',
        },
        lazy: lazyRoute(() => import('../../presentation/console/pages/SubscribersPage.vue')),
      },
      {
        path: 'access',
        id: 'access',
        handle: { title: 'Access', description: 'What this session may do, and the workspaces it sees.' },
        lazy: lazyRoute(() => import('../../presentation/console/pages/AccessPage.vue')),
      },
      ...RemovedPages.map((path) => ({ path, redirect: { name: 'sources' } })),
    ],
  },
];

/** Creates the app's router over `history`: the browser's in the app, memory in tests. */
export function createWebRouter(history: RouterHistoryMode = RouterHistoryMode.Browser) {
  return createAppRouter(routes, { titleSuffix: 'SCFD', routePersistence: false, history });
}
