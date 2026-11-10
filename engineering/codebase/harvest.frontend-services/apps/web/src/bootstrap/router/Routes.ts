import type { RouteLocationNormalizedLoaded } from 'vue-router';
import { createAppRouter, lazyPage, lazyRoute, RouterHistoryMode, type RouteConfig } from '@wow-two-beta/fe/router';
import ConsoleSession from '../ConsoleSession.vue';

/** Reads one query value, or nothing. @internal */
function query(route: RouteLocationNormalizedLoaded, key: string) {
  const value = route.query[key];
  const item = Array.isArray(value) ? value[0] : value;
  return typeof item === 'string' && item ? item : undefined;
}

/**
 * Holds the route table: one console layout route, and a page per place in SCFD's flow — Home; Sources, Pipelines (and
 * one pipeline), Runs; Posts; Subscriptions; Hosts, Access. Each title matches its sidebar label.
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
        handle: {
          title: 'Home',
          description: 'How the open container is doing: runs, failures, queued work and delivery.',
        },
        lazy: lazyRoute(() => import('../../presentation/console/pages/HomePage.vue')),
      },
      {
        path: 'sources',
        id: 'sources',
        handle: {
          title: 'Sources',
          description: 'What the installed source adapters registered, and the sections each one can follow.',
        },
        lazy: lazyRoute(() => import('../../presentation/console/pages/SourcesPage.vue')),
      },
      {
        path: 'pipelines',
        id: 'pipelines',
        handle: {
          title: 'Pipelines',
          description: 'Standing harvests, each following one section of a source on its own schedule.',
        },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/PipelinesPage.vue'),
          (route) => ({ newSource: query(route, 'source') }),
        ),
      },
      {
        path: 'pipelines/:id',
        id: 'pipeline',
        handle: { title: 'Pipeline', description: 'Its configuration, its schedule and its runs.' },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/PipelinePage.vue'),
          (route) => ({ pipelineId: String(route.params['id'] ?? '') }),
        ),
      },
      {
        path: 'runs',
        id: 'runs',
        handle: { title: 'Runs', description: 'Every pipeline’s latest executions and their outcomes.' },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/RunsPage.vue'),
          (route) => ({ pipeline: query(route, 'pipeline') }),
        ),
      },
      {
        path: 'posts',
        id: 'posts',
        handle: { title: 'Posts', description: 'What the pipelines captured, with source links and field evidence.' },
        lazy: lazyRoute(() => import('../../presentation/console/pages/PostsPage.vue')),
      },
      {
        path: 'subscriptions',
        id: 'subscriptions',
        handle: {
          title: 'Subscriptions',
          description: 'Which tenants receive which pipelines, and each subscription’s next delivery page.',
        },
        lazy: lazyRoute(() => import('../../presentation/console/pages/SubscriptionsPage.vue')),
      },
      {
        path: 'hosts',
        id: 'hosts',
        handle: { title: 'Hosts', description: 'Each source host lane: queued work, failures and pauses.' },
        lazy: lazyRoute(() => import('../../presentation/console/pages/HostsPage.vue')),
      },
      {
        path: 'access',
        id: 'access',
        handle: { title: 'Access', description: 'This session’s scopes, the tenants and the containers they see.' },
        lazy: lazyRoute(() => import('../../presentation/console/pages/AccessPage.vue')),
      },
    ],
  },
];

/** Creates the app's router over `history`: the browser's in the app, memory in tests. */
export function createWebRouter(history: RouterHistoryMode = RouterHistoryMode.Browser) {
  return createAppRouter(routes, { titleSuffix: 'SCFD', routePersistence: false, history });
}
