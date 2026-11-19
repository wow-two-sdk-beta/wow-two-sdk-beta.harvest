import { ApiFailureFactory, createApiClient, type ApiFailure } from '@wow-two-beta/fe/foundation/http';
import { ResultExtensions, type Result } from '@wow-two-beta/fe/foundation/results';
import {
  ApiKeyConstants,
  EntityRouteConstants,
  type CursorPage,
  type EntityCalendarEntryModel,
  type EntityCalendarQuery,
  type EntityEventModel,
  type EntityEventsQuery,
  type EntityPostModel,
  type EntityPostsQuery,
  type EntitySearchQuery,
  type EntitySummaryModel,
  type EntityTimelineModel,
} from '../contracts';
import type { RegistryClientOptions } from './CreateRegistryClient';

/**
 * Reads the entities posts are about — `IEntitiesBroker` over `api/entities` — inside one container: search, an entity
 * with its occurrences by year, its posts page by page, the calendar of occurrences and the change feed. Every read
 * needs `posts:read`; another tenant's container, or an entity of another container, answers 404.
 */
export interface EntitiesClient {
  /** Finds entities by name or alias, best first; newest first without `q`. */
  search(
    containerId: string,
    query?: EntitySearchQuery,
    signal?: AbortSignal,
  ): Promise<Result<readonly EntitySummaryModel[], ApiFailure>>;
  /** Reads an entity with its occurrences by year; a merged id reads as the entity it went into. */
  get(containerId: string, entityId: string, signal?: AbortSignal): Promise<Result<EntityTimelineModel, ApiFailure>>;
  /** Reads one page of an entity's posts from every source, newest posted first. */
  posts(
    containerId: string,
    entityId: string,
    query?: EntityPostsQuery,
    signal?: AbortSignal,
  ): Promise<Result<CursorPage<EntityPostModel>, ApiFailure>>;
  /** Reads the occurrences overlapping a window of up to 366 days, earliest first, predicted ones marked. */
  calendar(
    containerId: string,
    query: EntityCalendarQuery,
    signal?: AbortSignal,
  ): Promise<Result<readonly EntityCalendarEntryModel[], ApiFailure>>;
  /** Reads one page of the container's entity change feed. */
  events(
    containerId: string,
    query?: EntityEventsQuery,
    signal?: AbortSignal,
  ): Promise<Result<CursorPage<EntityEventModel>, ApiFailure>>;
}

/** Accepts a list whose items carry the string field `key`. @internal */
function keyedList<T>(key: string) {
  return (value: unknown): Result<readonly T[], ApiFailure> =>
    Array.isArray(value) &&
    value.every(
      (item) => item !== null && typeof item === 'object' && typeof (item as Record<string, unknown>)[key] === 'string',
    )
      ? ResultExtensions.ok(value as readonly T[])
      : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Accepts a `{ data, next, hasMore }` page whose items carry the string field `key`. @internal */
function cursorPage<T>(key: string) {
  return (value: unknown): Result<CursorPage<T>, ApiFailure> => {
    const page = value as Partial<CursorPage<T>> | null;
    return page !== null &&
      typeof page === 'object' &&
      keyedList<T>(key)(page.data).ok &&
      typeof page.next === 'string' &&
      typeof page.hasMore === 'boolean'
      ? ResultExtensions.ok(page as CursorPage<T>)
      : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
  };
}

/** Accepts an entity timeline with its years. @internal */
function timeline(value: unknown): Result<EntityTimelineModel, ApiFailure> {
  const row = value as Partial<EntityTimelineModel> | null;
  return row !== null &&
    typeof row === 'object' &&
    typeof row.id === 'string' &&
    typeof row.name === 'string' &&
    Array.isArray(row.years)
    ? ResultExtensions.ok(row as EntityTimelineModel)
    : ResultExtensions.fail(ApiFailureFactory.create('protocol'));
}

/** Drops the query values a caller left out, and turns the rest into strings. @internal */
function compact(values: Readonly<Record<string, string | number | undefined>>): Record<string, string> {
  return Object.fromEntries(
    Object.entries(values)
      .filter(([, value]) => value !== undefined && value !== '')
      .map(([key, value]) => [key, String(value)]),
  );
}

/** Creates the entity reads client for the API at `baseUrl` (same origin by default). */
export function createEntitiesClient(options: RegistryClientOptions = {}): EntitiesClient {
  const api = createApiClient({
    baseUrl: options.baseUrl?.replace(/\/+$/, '') ?? '',
    ...(options.apiKey !== undefined ? { defaultHeaders: { [ApiKeyConstants.headerName]: options.apiKey } } : {}),
    ...(options.credentials !== undefined ? { credentials: options.credentials } : {}),
    ...(options.fetch !== undefined ? { fetch: options.fetch } : {}),
    ...(options.scope !== undefined ? { scope: options.scope } : {}),
    ...(options.onUnauthorized !== undefined ? { onUnauthorized: options.onUnauthorized } : {}),
    ...(options.antiforgery !== undefined ? { antiforgery: options.antiforgery } : {}),
    retry: false,
  });
  const root = `/${EntityRouteConstants.entities}`;
  const signalOf = (signal?: AbortSignal) => (signal === undefined ? {} : { signal });
  return {
    search: (containerId, query = {}, signal) =>
      api.get(root, {
        query: compact({ containerId, q: query.q?.trim(), type: query.type, domain: query.domain, limit: query.limit }),
        ...signalOf(signal),
        decode: keyedList<EntitySummaryModel>('id'),
      }),
    get: (containerId, entityId, signal) =>
      api.get(`${root}/${encodeURIComponent(entityId)}`, {
        query: { containerId },
        ...signalOf(signal),
        decode: timeline,
      }),
    posts: (containerId, entityId, query = {}, signal) =>
      api.get(`${root}/${encodeURIComponent(entityId)}/${EntityRouteConstants.posts}`, {
        query: compact({ containerId, year: query.year, role: query.role, limit: query.limit, cursor: query.cursor }),
        ...signalOf(signal),
        decode: cursorPage<EntityPostModel>('postId'),
      }),
    calendar: (containerId, query, signal) =>
      api.get(`${root}/${EntityRouteConstants.calendar}`, {
        query: compact({ containerId, from: query.from, to: query.to, domain: query.domain, type: query.type }),
        ...signalOf(signal),
        decode: keyedList<EntityCalendarEntryModel>('entityId'),
      }),
    events: (containerId, query = {}, signal) =>
      api.get(`${root}/${EntityRouteConstants.events}`, {
        query: compact({
          containerId,
          cursor: query.cursor,
          limit: query.limit,
          type: query.type,
          domain: query.domain,
        }),
        ...signalOf(signal),
        decode: cursorPage<EntityEventModel>('eventId'),
      }),
  };
}
