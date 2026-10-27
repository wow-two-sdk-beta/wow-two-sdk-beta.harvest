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
} from '../contracts';
import { createRegistryClient, type RegistryClient, type RegistryClientOptions } from './CreateRegistryClient';

/** Exposes console operations under the same contract for an embedded or remote Harvest. */
export interface HarvestClient extends RegistryClient {
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

/** Builds an authenticated, form-agnostic console client. Credentials stay only in the caller's memory. */
export function createHarvestClient(options: RegistryClientOptions = {}): HarvestClient {
  const api = createApiClient({
    baseUrl: options.baseUrl?.replace(/\/+$/, '') ?? '',
    ...(options.apiKey !== undefined ? { defaultHeaders: { 'X-Api-Key': options.apiKey } } : {}),
    ...(options.credentials !== undefined ? { credentials: options.credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
    retry: false,
  });
  const signalOf = (signal?: AbortSignal) => (signal === undefined ? {} : { signal });
  return {
    ...createRegistryClient(options),
    listTenants: (signal) => api.get('api/tenants', { ...signalOf(signal), decode: list<TenantModel> }),
    registerTenant: (body, signal) =>
      api.post('api/tenants', { body, ...signalOf(signal), decode: object<TenantModel> }),
    createContainer: (body, signal) =>
      api.post('api/containers', { body, ...signalOf(signal), decode: object<ContainerModel> }),
    registerSource: (body, signal) =>
      api.post('api/sources', { body, ...signalOf(signal), decode: object<SourceModel> }),
    declarePipeline: (body, signal) =>
      api.post('api/pipelines', { body, ...signalOf(signal), decode: object<PipelineModel> }),
    updatePipeline: (id, body, signal) =>
      api.put(`api/pipelines/${encodeURIComponent(id)}`, { body, ...signalOf(signal), decode: object<PipelineModel> }),
    setPipelinePaused: (id, paused, signal) =>
      api.post(`api/pipelines/${encodeURIComponent(id)}/pause`, {
        body: { paused },
        ...signalOf(signal),
        decode: object<PipelineModel>,
      }),
    startRun: (pipelineId, kind = 'capture', signal) =>
      api.post('api/runs', {
        body: { pipelineId, kind },
        ...signalOf(signal),
        decode: object<RunModel>,
      }),
    cancelRun: (id, signal) =>
      api.post(`api/runs/${encodeURIComponent(id)}/cancel`, { ...signalOf(signal), decode: object<RunModel> }),
    listRuns: (pipelineId, signal) =>
      api.get('api/runs', { query: { pipelineId }, ...signalOf(signal), decode: list<RunModel> }),
    listHosts: (containerId, signal) =>
      api.get('api/hosts', { query: { containerId }, ...signalOf(signal), decode: list<HostHealthModel> }),
    listPosts: (containerId, signal) =>
      api.get('api/posts', { query: { containerId }, ...signalOf(signal), decode: list<PostModel> }),
    listSubscriptions: (signal) =>
      api.get('api/subscriptions', { ...signalOf(signal), decode: list<SubscriptionModel> }),
    subscribe: (body, signal) =>
      api.post('api/subscriptions', { body, ...signalOf(signal), decode: object<SubscriptionModel> }),
    pull: (id, cursor, signal) =>
      api.get(`api/subscriptions/${encodeURIComponent(id)}/events`, {
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
  };
}
