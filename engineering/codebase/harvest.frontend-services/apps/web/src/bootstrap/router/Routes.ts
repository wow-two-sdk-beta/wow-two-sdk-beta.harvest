import type { RouteLocationNormalizedLoaded } from 'vue-router';
import { createAppRouter, lazyPage, lazyRoute, RouterHistoryMode, type RouteConfig } from '@wow-two-beta/fe/router';
import type { PipelineTab, SourceTab } from '@wow-two-beta/fe-harvest/admin';
import type { PipelineStageKind } from '@wow-two-beta/fe-harvest/contracts';
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
const RemovedPages = ['runs', 'posts', 'subscriptions', 'hosts'];

const PipelinePath = 'pipelines/:id';
const StagePath = `${PipelinePath}/stages/:stage(discover|harvest|process)`;

function pipelinePage(path: string, id: string, title: string, tab: PipelineTab = 'overview') {
  return {
    path,
    id,
    handle: { title },
    lazy: lazyPage(
      () => import('../../presentation/console/pages/PipelinePage.vue'),
      (route) => ({
        pipelineId: String(route.params['id'] ?? ''),
        tab,
        ...(typeof route.params['stage'] === 'string' ? { stage: route.params['stage'] as PipelineStageKind } : {}),
        ...(typeof route.params['runId'] === 'string' ? { runId: route.params['runId'] } : {}),
      }),
    ),
  };
}

/** The operator's pages and their direct links within the console layout. */
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
      sourceTab('sources/:id/processed', 'source-processed', 'Processed', 'processed'),
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
      sourceTab('sources/:id/pipelines', 'source-pipelines', 'Pipelines', 'pipelines'),
      {
        path: 'sources/:id/pipelines/:runId',
        id: 'source-run',
        handle: { title: 'Run audit' },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/RunPage.vue'),
          (route) => ({ sourceId: String(route.params['id'] ?? ''), runId: String(route.params['runId'] ?? '') }),
        ),
      },
      sourceTab('sources/:id/settings', 'source-settings', 'Settings', 'settings'),
      {
        path: 'pipelines',
        id: 'pipelines',
        handle: { title: 'Pipelines', description: 'Monitor runs and open a pipeline to inspect its stages.' },
        lazy: lazyRoute(() => import('../../presentation/console/pages/PipelinesPage.vue')),
      },
      pipelinePage(PipelinePath, 'pipeline', 'Pipeline'),
      pipelinePage(`${PipelinePath}/logs`, 'pipeline-logs', 'Logs', 'logs'),
      pipelinePage(`${PipelinePath}/discoveries`, 'pipeline-discoveries', 'Discoveries', 'discoveries'),
      pipelinePage(`${PipelinePath}/runs/:runId`, 'pipeline-run', 'Run audit', 'logs'),
      pipelinePage(StagePath, 'pipeline-stage', 'Stage details'),
      pipelinePage(`${StagePath}/logs`, 'pipeline-stage-logs', 'Logs', 'logs'),
      pipelinePage(`${StagePath}/discoveries`, 'pipeline-stage-discoveries', 'Discoveries', 'discoveries'),
      pipelinePage(`${StagePath}/runs/:runId`, 'pipeline-stage-run', 'Run audit', 'logs'),
      {
        path: 'entities',
        id: 'entities',
        handle: {
          title: 'Entities',
          description: 'The events and listings posts are about, each across its years and sources.',
        },
        lazy: lazyRoute(() => import('../../presentation/console/pages/EntitiesPage.vue')),
      },
      {
        path: 'entities/:id',
        id: 'entity',
        handle: { title: 'Entity' },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/EntityPage.vue'),
          (route) => ({ entityId: String(route.params['id'] ?? '') }),
        ),
      },
      {
        path: 'subscribers',
        id: 'subscribers',
        handle: {
          title: 'Subscribers',
          description: 'The products that receive posts, and the source and section each one follows.',
        },
        lazy: lazyPage(
          () => import('../../presentation/console/pages/SubscribersPage.vue'),
          (route) => ({ section: typeof route.query['section'] === 'string' ? route.query['section'] : undefined }),
        ),
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
