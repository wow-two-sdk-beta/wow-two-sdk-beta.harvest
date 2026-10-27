import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import {
  ApiKeyConstants,
  RegistrationRouteConstants,
  type ContainerModel,
  type PipelineModel,
  type SourceModel,
} from '../contracts';

/** Configures where the registry is reached and with which key. */
export interface RegistryClientOptions {
  /** The origin serving the API: empty for the consumer's own backend (Form 1), the service's URL otherwise. */
  readonly baseUrl?: string;
  /** The Harvest API key (`hv_…`) sent in `X-Api-Key`; its scopes decide what the service answers. */
  readonly apiKey?: string;
  /** Whether the browser sends cookies cross-origin; the service's CORS policy must allow it. */
  readonly credentials?: RequestCredentials;
  /** Replaces the global fetch, for tests. */
  readonly fetch?: typeof globalThis.fetch;
}

/** Reads Harvest's registry over HTTP. */
export interface RegistryClient {
  /** Lists the containers the key may see: every one for the operator, the pool and its own for a tenant. */
  listContainers(signal?: AbortSignal): Promise<Result<readonly ContainerModel[], ApiFailure>>;
  /** Lists a container's sources. */
  listSources(containerId: string, signal?: AbortSignal): Promise<Result<readonly SourceModel[], ApiFailure>>;
  /** Lists a container's pipelines. */
  listPipelines(containerId: string, signal?: AbortSignal): Promise<Result<readonly PipelineModel[], ApiFailure>>;
}

/** Accepts an array whose items carry a string `id`; the shapes behind it are the be contract's. */
function decodeList<T>(value: unknown) {
  return Array.isArray(value) &&
    value.every((item) => item !== null && typeof item === 'object' && 'id' in item && typeof item.id === 'string')
    ? ResultExtensions.ok<readonly T[]>(value as readonly T[])
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Creates a client for the registry at `baseUrl` (same origin by default). */
export function createRegistryClient(options: RegistryClientOptions = {}): RegistryClient {
  const client = createApiClient({
    baseUrl: options.baseUrl?.replace(/\/+$/, '') ?? '',
    ...(options.apiKey !== undefined ? { defaultHeaders: { [ApiKeyConstants.headerName]: options.apiKey } } : {}),
    ...(options.credentials !== undefined ? { credentials: options.credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
  });
  const byContainer = (containerId: string) => ({ [RegistrationRouteConstants.containerIdParameter]: containerId });

  return {
    listContainers(signal) {
      return client.get(RegistrationRouteConstants.containers, {
        ...(signal !== undefined ? { signal } : {}),
        decode: decodeList<ContainerModel>,
      });
    },
    listSources(containerId, signal) {
      return client.get(RegistrationRouteConstants.sources, {
        query: byContainer(containerId),
        ...(signal !== undefined ? { signal } : {}),
        decode: decodeList<SourceModel>,
      });
    },
    listPipelines(containerId, signal) {
      return client.get(RegistrationRouteConstants.pipelines, {
        query: byContainer(containerId),
        ...(signal !== undefined ? { signal } : {}),
        decode: decodeList<PipelineModel>,
      });
    },
  };
}
