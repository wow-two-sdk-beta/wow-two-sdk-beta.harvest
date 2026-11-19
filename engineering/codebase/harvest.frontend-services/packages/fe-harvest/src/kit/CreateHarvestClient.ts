import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import type {
  ContainerModel,
  SourceModel,
  PipelineModel,
  PipelineUpdate,
  TenantModel,
  RunModel,
  HostHealthModel,
  PostModel,
  SubscriptionModel,
  CursorPage,
  DeliveryEvent,
  WorkKind,
  SourceCapabilitiesModel,
  SourceOverviewModel,
  SourceGroupModel,
  SourceGroupDraft,
  SourceUpdate,
  SourceReadingUpdate,
  SourceHealthModel,
  SourcePostModel,
  SourcePostPage,
  SourcePostQuery,
  PostRevisionModel,
  PostProcessingModel,
  SourceProcessedPage,
  SourceProcessedQuery,
} from '../contracts';
import { createEntitiesClient, type EntitiesClient } from './CreateEntitiesClient';
import { createRegistryClient, type RegistryClient, type RegistryClientOptions } from './CreateRegistryClient';
import type { ScheduleModel } from '../contracts/PipelineModel';
import type {
  DiscoveryState,
  PipelineDiscoveryModel,
  PipelineStageModel,
  RunAttemptModel,
} from '../contracts/PipelineStagesModel';

/** Reads and configures the standalone discovery, harvest and processing stages. */
export interface PipelineStagesClient {
  list(pipelineId: string, signal?: AbortSignal): Promise<Result<readonly PipelineStageModel[], ApiFailure>>;
  save(
    pipelineId: string,
    body: Omit<ScheduleModel, 'id' | 'nextDueAtUtc'>,
    signal?: AbortSignal,
  ): Promise<Result<boolean, ApiFailure>>;
  discoveries(
    pipelineId: string,
    offset: number,
    limit: number,
    state?: DiscoveryState,
    signal?: AbortSignal,
  ): Promise<Result<readonly PipelineDiscoveryModel[], ApiFailure>>;
  run(runId: string, signal?: AbortSignal): Promise<Result<RunModel, ApiFailure>>;
  audit(
    runId: string,
    offset: number,
    limit: number,
    signal?: AbortSignal,
    newestFirst?: boolean,
  ): Promise<Result<readonly RunAttemptModel[], ApiFailure>>;
}

/** Exposes console operations under the same contract for an embedded or remote Harvest. */
export interface HarvestClient extends RegistryClient {
  readonly pipelineStages?: PipelineStagesClient;
  /** Reads the entities posts are about; absent on a client that predates the entity reads. */
  readonly entities?: EntitiesClient;
  /** Reads private source bytes with the same credentials and session lifetime as other API calls. */
  getSourceAsset?(sourceId: string, assetId: string, signal?: AbortSignal): Promise<Result<Blob, ApiFailure>>;
  getSourceCapabilities(id: string, signal?: AbortSignal): Promise<Result<SourceCapabilitiesModel, ApiFailure>>;
  listTenants(signal?: AbortSignal): Promise<Result<readonly TenantModel[], ApiFailure>>;
  registerTenant(body: { slug: string; name: string }, signal?: AbortSignal): Promise<Result<TenantModel, ApiFailure>>;
  createContainer(
    body: { kind: 'pool' | 'private'; tenantId?: string; externalRef?: string },
    signal?: AbortSignal,
  ): Promise<Result<ContainerModel, ApiFailure>>;
  registerSource(
    body: Readonly<Record<string, unknown>>,
    signal?: AbortSignal,
  ): Promise<Result<SourceModel, ApiFailure>>;
  declarePipeline(
    body: Readonly<Record<string, unknown>>,
    signal?: AbortSignal,
  ): Promise<Result<PipelineModel, ApiFailure>>;
  setPipelinePaused(id: string, paused: boolean, signal?: AbortSignal): Promise<Result<PipelineModel, ApiFailure>>;
  updatePipeline(id: string, body: PipelineUpdate, signal?: AbortSignal): Promise<Result<PipelineModel, ApiFailure>>;
  startRun(pipelineId: string, kind?: WorkKind, signal?: AbortSignal): Promise<Result<RunModel, ApiFailure>>;
  cancelRun(id: string, signal?: AbortSignal): Promise<Result<RunModel, ApiFailure>>;
  listRuns(pipelineId: string, signal?: AbortSignal): Promise<Result<readonly RunModel[], ApiFailure>>;
  listHosts(containerId: string, signal?: AbortSignal): Promise<Result<readonly HostHealthModel[], ApiFailure>>;
  listPosts(containerId: string, signal?: AbortSignal): Promise<Result<readonly PostModel[], ApiFailure>>;
  listSubscriptions(signal?: AbortSignal): Promise<Result<readonly SubscriptionModel[], ApiFailure>>;
  subscribe(
    body: {
      tenantId: string;
      containerId: string;
      pipelineId: string;
      schema?: string;
      filterKey?: string;
      context?: Readonly<Record<string, string>>;
    },
    signal?: AbortSignal,
  ): Promise<Result<SubscriptionModel, ApiFailure>>;
  pull(id: string, cursor?: string, signal?: AbortSignal): Promise<Result<CursorPage<DeliveryEvent>, ApiFailure>>;
  /** Reads every source of a container with its sections, last harvest, 14-day trend and health. */
  listSourceOverview(
    containerId: string,
    signal?: AbortSignal,
  ): Promise<Result<readonly SourceOverviewModel[], ApiFailure>>;
  /** Reads the groups a container's sources are filed under. */
  listSourceGroups(containerId: string, signal?: AbortSignal): Promise<Result<readonly SourceGroupModel[], ApiFailure>>;
  /** Declares a group, idempotent by name. */
  createSourceGroup(body: SourceGroupDraft, signal?: AbortSignal): Promise<Result<SourceGroupModel, ApiFailure>>;
  /** Sets a source's display name and group. */
  updateSource(sourceId: string, body: SourceUpdate, signal?: AbortSignal): Promise<Result<SourceModel, ApiFailure>>;
  /** Sets the gap between two page reads of a source's host; answers with the source's health. */
  setSourceReading(
    sourceId: string,
    body: SourceReadingUpdate,
    signal?: AbortSignal,
  ): Promise<Result<SourceHealthModel, ApiFailure>>;
  /** Reads a source's posts, newest first. */
  listSourcePosts(
    sourceId: string,
    limit?: number,
    signal?: AbortSignal,
  ): Promise<Result<readonly SourcePostModel[], ApiFailure>>;
  /** Searches saved posts before paging, with counts over the entire matching source. */
  querySourcePosts(
    sourceId: string,
    query: SourcePostQuery,
    signal?: AbortSignal,
  ): Promise<Result<SourcePostPage, ApiFailure>>;
  /** Reads one post with its links, inside the container that holds it. */
  getPost(postId: string, containerId: string, signal?: AbortSignal): Promise<Result<PostModel, ApiFailure>>;
  /** Reads a post's revisions, newest first; empty for a post older than revision tracking. */
  listPostRevisions(postId: string, signal?: AbortSignal): Promise<Result<readonly PostRevisionModel[], ApiFailure>>;
  /** Reads one page of a source's processed posts: tags, verdict and extraction summary, newest first seen first. */
  querySourceProcessed?(
    sourceId: string,
    query: SourceProcessedQuery,
    signal?: AbortSignal,
  ): Promise<Result<SourceProcessedPage, ApiFailure>>;
  /** Reads saved processing results within the post's container, optionally for one source. */
  listPostProcessing?(
    postId: string,
    containerId: string,
    signal?: AbortSignal,
    sourceId?: string,
  ): Promise<Result<readonly PostProcessingModel[], ApiFailure>>;
}

/** Rejects a malformed object at the transport boundary. */
function object<T>(value: unknown): Result<T, ApiFailure> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? ResultExtensions.ok(value as T)
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Rejects a malformed list at the transport boundary. */
function list<T>(value: unknown): Result<readonly T[], ApiFailure> {
  return Array.isArray(value) && value.every((item) => item !== null && typeof item === 'object')
    ? ResultExtensions.ok(value as readonly T[])
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Accepts a list whose items carry the string field `key`. */
function keyed<T>(key: string) {
  return (value: unknown): Result<readonly T[], ApiFailure> =>
    Array.isArray(value) &&
    value.every(
      (item) => item !== null && typeof item === 'object' && typeof (item as Record<string, unknown>)[key] === 'string',
    )
      ? ResultExtensions.ok(value as readonly T[])
      : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Validates the overview's fields the Sources area reads before it draws a card. */
function sourceOverviews(value: unknown): Result<readonly SourceOverviewModel[], ApiFailure> {
  const ok =
    Array.isArray(value) &&
    value.every((item) => {
      const row = item as Partial<SourceOverviewModel> | null;
      return (
        row !== null &&
        typeof row === 'object' &&
        typeof row.sourceId === 'string' &&
        typeof row.name === 'string' &&
        typeof row.state === 'string' &&
        Array.isArray(row.newPostsDaily) &&
        Array.isArray(row.sections) &&
        row.health !== null &&
        typeof row.health === 'object'
      );
    });
  return ok
    ? ResultExtensions.ok(value as readonly SourceOverviewModel[])
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Validates discovery before presentation uses the runtime's configuration fields. */
function sourceCapabilities(value: unknown): Result<SourceCapabilitiesModel, ApiFailure> {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    return ResultExtensions.fail(ApiFailureFactory.create('protocol'));
  const row = value as Partial<SourceCapabilitiesModel>;
  const configuration = row.configuration;
  const kinds = ['capture', 'discover', 'harvest', 'process', 'refresh', 'sweep', 'check', 'metrics', 'link', 'intake'];
  return typeof row.sourceId === 'string' &&
    Array.isArray(row.workKinds) &&
    row.workKinds.every((kind) => kinds.includes(kind)) &&
    configuration &&
    typeof configuration.displayName === 'string' &&
    ['ready', 'configurationRequired', 'unavailable'].includes(configuration.readiness) &&
    configuration.scopeSchema &&
    typeof configuration.scopeSchema === 'object' &&
    !Array.isArray(configuration.scopeSchema)
    ? ResultExtensions.ok(row as SourceCapabilitiesModel)
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

function postPage(value: unknown): Result<SourcePostPage, ApiFailure> {
  const page = value as Partial<SourcePostPage> | null;
  const countOf = (value: unknown): value is number =>
    typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
  const valid =
    page &&
    keyed<SourcePostModel>('postId')(page.items).ok &&
    (page.nextCursor == null || typeof page.nextCursor === 'string') &&
    countOf(page.total) &&
    countOf(page.searchTotal) &&
    countOf(page.defaultPageSize) &&
    page.defaultPageSize > 0 &&
    countOf(page.maxPageSize) &&
    page.maxPageSize >= page.defaultPageSize &&
    Array.isArray(page.facets) &&
    page.facets.every(
      (facet) =>
        facet &&
        typeof facet.key === 'string' &&
        typeof facet.label === 'string' &&
        Array.isArray(facet.options) &&
        facet.options.every(
          (option: { value?: unknown; label?: unknown; count?: unknown }) =>
            option && typeof option.value === 'string' && typeof option.label === 'string' && countOf(option.count),
        ),
    );
  return valid
    ? ResultExtensions.ok(page as SourcePostPage)
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Validates a processed page's items and counts before a card reads them. */
function processedPage(value: unknown): Result<SourceProcessedPage, ApiFailure> {
  const page = value as Partial<SourceProcessedPage> | null;
  const valid =
    page &&
    Array.isArray(page.items) &&
    page.items.every(
      (item) =>
        item &&
        typeof item.postId === 'string' &&
        typeof item.title === 'string' &&
        item.tags !== null &&
        typeof item.tags === 'object' &&
        typeof item.tags.status === 'string' &&
        Array.isArray(item.tags.domains),
    ) &&
    (page.nextCursor == null || typeof page.nextCursor === 'string') &&
    typeof page.total === 'number' &&
    Number.isSafeInteger(page.total) &&
    page.total >= 0;
  return valid
    ? ResultExtensions.ok(page as SourceProcessedPage)
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Writes a processed query as the route's query string, leaving out what the caller did not set. */
function processedQuery(query: SourceProcessedQuery): Record<string, string> {
  return Object.fromEntries(
    Object.entries({
      entityType: query.entityType,
      verdict: query.verdict,
      extracted: query.extracted === undefined ? undefined : String(query.extracted),
      limit: query.limit === undefined ? undefined : String(query.limit),
      cursor: query.cursor,
    }).filter((entry): entry is [string, string] => entry[1] !== undefined && entry[1] !== ''),
  );
}

/** Builds an authenticated, form-agnostic console client. Credentials stay only in the caller's memory. */
export function createHarvestClient(options: RegistryClientOptions = {}): HarvestClient {
  const api = createApiClient({
    baseUrl: options.baseUrl?.replace(/\/+$/, '') ?? '',
    ...(options.apiKey !== undefined ? { defaultHeaders: { 'X-Api-Key': options.apiKey } } : {}),
    ...(options.credentials !== undefined ? { credentials: options.credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
    ...(options.scope !== undefined ? { scope: options.scope } : {}),
    ...(options.onUnauthorized !== undefined ? { onUnauthorized: options.onUnauthorized } : {}),
    ...(options.antiforgery !== undefined ? { antiforgery: options.antiforgery } : {}),
    retry: false,
  });
  const signalOf = (signal?: AbortSignal) => (signal === undefined ? {} : { signal });
  return {
    ...createRegistryClient(options),
    entities: createEntitiesClient(options),
    getSourceAsset: (sourceId, assetId, signal) =>
      api.get(`/api/sources/${encodeURIComponent(sourceId)}/assets/${encodeURIComponent(assetId)}`, {
        ...signalOf(signal),
        response: 'blob',
      }),
    pipelineStages: {
      list: (id, signal) =>
        api.get(`/api/pipelines/${encodeURIComponent(id)}/stages`, {
          ...signalOf(signal),
          decode: list<PipelineStageModel>,
        }),
      save: (id, body, signal) =>
        api.put(`/api/pipelines/${encodeURIComponent(id)}/stages`, {
          body,
          ...signalOf(signal),
          decode: (value) =>
            typeof value === 'boolean'
              ? ResultExtensions.ok(value)
              : ResultExtensions.fail(ApiFailureFactory.create('protocol')),
        }),
      discoveries: (id, offset, limit, state, signal) =>
        api.get(`/api/pipelines/${encodeURIComponent(id)}/discoveries`, {
          query: { offset: String(offset), limit: String(limit), ...(state === undefined ? {} : { state }) },
          ...signalOf(signal),
          decode: list<PipelineDiscoveryModel>,
        }),
      run: (id, signal) =>
        api.get(`/api/runs/${encodeURIComponent(id)}`, { ...signalOf(signal), decode: object<RunModel> }),
      audit: (id, offset, limit, signal, newestFirst) =>
        api.get(`/api/runs/${encodeURIComponent(id)}/audit`, {
          query: { offset: String(offset), limit: String(limit), ...(newestFirst ? { newestFirst: 'true' } : {}) },
          ...signalOf(signal),
          decode: list<RunAttemptModel>,
        }),
    },
    getSourceCapabilities: (id, signal) =>
      api.get(`/api/sources/${encodeURIComponent(id)}/capabilities`, {
        ...signalOf(signal),
        decode: sourceCapabilities,
      }),
    listTenants: (signal) => api.get('/api/tenants', { ...signalOf(signal), decode: list<TenantModel> }),
    registerTenant: (body, signal) =>
      api.post('/api/tenants', { body, ...signalOf(signal), decode: object<TenantModel> }),
    createContainer: (body, signal) =>
      api.post('/api/containers', { body, ...signalOf(signal), decode: object<ContainerModel> }),
    registerSource: (body, signal) =>
      api.post('/api/sources', { body, ...signalOf(signal), decode: object<SourceModel> }),
    declarePipeline: (body, signal) =>
      api.post('/api/pipelines', { body, ...signalOf(signal), decode: object<PipelineModel> }),
    updatePipeline: (id, body, signal) =>
      api.put(`/api/pipelines/${encodeURIComponent(id)}`, { body, ...signalOf(signal), decode: object<PipelineModel> }),
    setPipelinePaused: (id, paused, signal) =>
      api.post(`/api/pipelines/${encodeURIComponent(id)}/pause`, {
        body: { paused },
        ...signalOf(signal),
        decode: object<PipelineModel>,
      }),
    startRun: (pipelineId, kind = 'capture', signal) =>
      api.post('/api/runs', {
        body: { pipelineId, kind },
        ...signalOf(signal),
        decode: object<RunModel>,
      }),
    cancelRun: (id, signal) =>
      api.post(`/api/runs/${encodeURIComponent(id)}/cancel`, { ...signalOf(signal), decode: object<RunModel> }),
    listRuns: (pipelineId, signal) =>
      api.get('/api/runs', { query: { pipelineId }, ...signalOf(signal), decode: list<RunModel> }),
    listHosts: (containerId, signal) =>
      api.get('/api/hosts', { query: { containerId }, ...signalOf(signal), decode: list<HostHealthModel> }),
    listPosts: (containerId, signal) =>
      api.get('/api/posts', { query: { containerId }, ...signalOf(signal), decode: list<PostModel> }),
    listSubscriptions: (signal) =>
      api.get('/api/subscriptions', { ...signalOf(signal), decode: list<SubscriptionModel> }),
    subscribe: (body, signal) =>
      api.post('/api/subscriptions', { body, ...signalOf(signal), decode: object<SubscriptionModel> }),
    pull: (id, cursor, signal) =>
      api.get(`/api/subscriptions/${encodeURIComponent(id)}/events`, {
        query: cursor ? { cursor } : {},
        ...signalOf(signal),
        decode(value) {
          if (
            value === null ||
            typeof value !== 'object' ||
            !('data' in value) ||
            !Array.isArray(value.data) ||
            !('next' in value) ||
            typeof value.next !== 'string' ||
            !('hasMore' in value) ||
            typeof value.hasMore !== 'boolean'
          )
            return ResultExtensions.fail(ApiFailureFactory.create('protocol'));
          return ResultExtensions.ok(value as unknown as CursorPage<DeliveryEvent>);
        },
      }),
    listSourceOverview: (containerId, signal) =>
      api.get('/api/sources/overview', { query: { containerId }, ...signalOf(signal), decode: sourceOverviews }),
    listSourceGroups: (containerId, signal) =>
      api.get('/api/sources/groups', { query: { containerId }, ...signalOf(signal), decode: list<SourceGroupModel> }),
    createSourceGroup: (body, signal) =>
      api.post('/api/sources/groups', { body, ...signalOf(signal), decode: object<SourceGroupModel> }),
    updateSource: (sourceId, body, signal) =>
      api.put(`/api/sources/${encodeURIComponent(sourceId)}`, {
        body,
        ...signalOf(signal),
        decode: object<SourceModel>,
      }),
    setSourceReading: (sourceId, body, signal) =>
      api.put(`/api/sources/${encodeURIComponent(sourceId)}/reading`, {
        body,
        ...signalOf(signal),
        decode: object<SourceHealthModel>,
      }),
    listSourcePosts: (sourceId, limit = 200, signal) =>
      api.get(`/api/sources/${encodeURIComponent(sourceId)}/posts`, {
        query: { limit: String(limit) },
        ...signalOf(signal),
        decode: keyed<SourcePostModel>('postId'),
      }),
    querySourcePosts: (sourceId, body, signal) =>
      api.post(`/api/sources/${encodeURIComponent(sourceId)}/posts/query`, {
        body,
        ...signalOf(signal),
        decode: postPage,
      }),
    getPost: (postId, containerId, signal) =>
      api.get(`/api/posts/${encodeURIComponent(postId)}`, {
        query: { containerId },
        ...signalOf(signal),
        decode: object<PostModel>,
      }),
    listPostRevisions: (postId, signal) =>
      api.get(`/api/posts/${encodeURIComponent(postId)}/revisions`, {
        ...signalOf(signal),
        decode: list<PostRevisionModel>,
      }),
    querySourceProcessed: (sourceId, query, signal) =>
      api.get(`/api/sources/${encodeURIComponent(sourceId)}/processed`, {
        query: processedQuery(query),
        ...signalOf(signal),
        decode: processedPage,
      }),
    listPostProcessing: (postId, containerId, signal, sourceId) =>
      api.get(`/api/posts/${encodeURIComponent(postId)}/processing`, {
        query: { containerId, ...(sourceId ? { sourceId } : {}), limit: '50' },
        ...signalOf(signal),
        decode: list<PostProcessingModel>,
      }),
  };
}
