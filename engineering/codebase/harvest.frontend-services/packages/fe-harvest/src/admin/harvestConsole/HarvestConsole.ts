import {
  computed,
  inject,
  onMounted,
  onScopeDispose,
  provide,
  ref,
  shallowRef,
  watch,
  type ComputedRef,
  type InjectionKey,
  type Ref,
  type ShallowRef,
} from 'vue';
import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import type { Result } from '@wow-two-beta/fe/foundation/results';
import type {
  ContainerModel,
  CursorPage,
  DeliveryEvent,
  HostHealthModel,
  PipelineModel,
  PipelineUpdate,
  PostModel,
  RunModel,
  SourceCapabilitiesModel,
  SourceModel,
  SubscriptionModel,
  TenantModel,
  WorkKind,
} from '../../contracts';
import { createHarvestClient, type HarvestClient } from '../../kit';
import { sourceSections } from './SourceSections';

/** Names a place in the console, so a panel can link to it without knowing the host app's routes. */
export type ConsolePlace =
  | { readonly kind: 'home' }
  | { readonly kind: 'sources' }
  | { readonly kind: 'pipelines'; readonly source?: string }
  | { readonly kind: 'pipeline'; readonly id: string }
  | { readonly kind: 'runs'; readonly pipeline?: string }
  | { readonly kind: 'posts' }
  | { readonly kind: 'subscriptions' }
  | { readonly kind: 'hosts' }
  | { readonly kind: 'access' };

/** Lets panels link to places: the host app resolves each place to its own route. */
export interface ConsoleNavigation {
  /** The place's address, for the link's `href`. */
  href(place: ConsolePlace): string;
  /** Opens the place in the app, without reloading the page. */
  go(place: ConsolePlace): void;
}

/** Configures one console session. Pass a reactive object, such as a component's props, to keep the fields live. */
export interface HarvestConsoleOptions {
  /** The connected client; without one the console waits for an API key or a sign-in. */
  readonly client?: HarvestClient;
  /** The embedding app owns the connection: the console offers no key form and no disconnect. */
  readonly connectionManaged?: boolean;
  /** Where an opted-in local tab keeps its key between reloads. */
  readonly connectionStorage?: Storage;
  /** The signed-in scopes; omitted, every action shows and the server decides. */
  readonly permissions?: readonly string[];
  /** Whether the signed-in account operates SCFD; a tenant writes only to its private container. */
  readonly isOperator?: boolean;
  /** Whether this server runs a capture worker; `false` explains why queued runs wait. */
  readonly localWorkerEnabled?: boolean;
  /** Resolves and opens places; omitted, panels show names without links. */
  readonly navigation?: ConsoleNavigation;
  /** How often the open container refreshes while the tab is visible. Default 5 seconds. */
  readonly refreshIntervalMs?: number;
}

/** The new pipeline a source and one of its sections describe. */
export interface PipelineDraft {
  readonly sourceId: string;
  readonly name?: string;
  /** The chosen section's URL; omitted, the pipeline follows the whole source. */
  readonly sectionUrl?: string;
  readonly everyMinutes: number;
  readonly scheduleEnabled: boolean;
}

/** The subscription a tenant takes on a pipeline. */
export interface SubscriptionDraft {
  readonly tenantId: string;
  readonly pipelineId: string;
  readonly tag?: string;
}

type ListState = 'loading' | 'ready' | 'unavailable';

const ConnectionKey = 'scfd.console.connection';
const DefaultRefreshMs = 5000;

/** Presents access failures without losing the server's actionable message. @internal */
function message(error: ApiFailure) {
  return error.status === 401 ? 'Sign in or reconnect to continue.' : error.message;
}

/** Orders runs newest first. @internal */
function newestFirst(a: RunModel, b: RunModel) {
  return (b.queuedAtUtc ?? '').localeCompare(a.queuedAtUtc ?? '');
}

/**
 * Creates the console's shared session: the connection, the open container and its data, refreshed on an interval,
 * and every mutation the panels run. Call it from a component's setup; `HarvestConsoleProvider` does.
 */
export function createHarvestConsole(options: HarvestConsoleOptions) {
  const client = shallowRef<HarvestClient | null>(options.client ?? null);
  const connecting = ref(client.value !== null);
  const connected = computed(() => client.value !== null && !connecting.value);
  const pending = ref(false);
  const failure = ref<string | null>(null);
  const notice = ref<string | null>(null);
  const containers = shallowRef<readonly ContainerModel[]>([]);
  const tenants = shallowRef<readonly TenantModel[]>([]);
  const containerId = ref('');
  /** True once the open container's first refresh finished. */
  const loaded = ref(false);
  const sources = shallowRef<readonly SourceModel[]>([]);
  const sourceListState = ref<ListState>('loading');
  const pipelines = shallowRef<readonly PipelineModel[]>([]);
  const runsByPipeline = shallowRef<Readonly<Record<string, readonly RunModel[]>>>({});
  const posts = shallowRef<readonly PostModel[]>([]);
  const hosts = shallowRef<readonly HostHealthModel[]>([]);
  const subscriptions = shallowRef<readonly SubscriptionModel[]>([]);
  const capabilities = shallowRef<Readonly<Record<string, SourceCapabilitiesModel>>>({});
  const capabilityErrors = shallowRef<Readonly<Record<string, string>>>({});

  let connectedApiKey = '';
  let read: AbortController | null = null;
  let action: AbortController | null = null;
  let capabilityRead = new AbortController();
  const capabilityRequested = new Set<string>();
  let inflight: Promise<void> | null = null;
  let again = false;
  let disposed = false;
  let timer: ReturnType<typeof setInterval> | null = null;

  const container = computed(() => containers.value.find((item) => item.id === containerId.value));
  /** Every loaded run of the open container, newest first. */
  const runs = computed(() =>
    Object.entries(runsByPipeline.value)
      .flatMap(([pipelineId, rows]) => rows.map((run) => (run.pipelineId ? run : { ...run, pipelineId })))
      .sort(newestFirst),
  );
  /** The subscriptions that follow a pipeline of the open container. */
  const visibleSubscriptions = computed(() =>
    subscriptions.value.filter((item) => pipelines.value.some((pipeline) => pipeline.id === item.pipelineId)),
  );
  const isOperator = computed(() => options.isOperator);
  const localWorkerEnabled = computed(() => options.localWorkerEnabled);
  const connectionManaged = computed(() => options.connectionManaged === true);

  /** Whether the session holds a scope; without a permission list the server decides. */
  function can(scope: string) {
    return options.permissions === undefined || options.permissions.includes(scope);
  }

  /** Whether the session may change the open container: an operator anywhere, a tenant in its private container. */
  function canWrite(scope: string) {
    return can(scope) && (options.isOperator !== false || container.value?.kind === 'private');
  }

  function sourceOf(id: string) {
    return sources.value.find((item) => item.id === id);
  }

  function pipelineOf(id: string) {
    return pipelines.value.find((item) => item.id === id);
  }

  function runsOf(pipelineId: string) {
    return runs.value.filter((run) => run.pipelineId === pipelineId);
  }

  /** The sections a source's adapter lists, on a runtime that enforces them. */
  function sectionsOf(sourceId: string) {
    return sourceSections(capabilities.value[sourceId]);
  }

  /** Whether a source's adapter is configured here and can capture. */
  function canCapture(sourceId: string) {
    const metadata = capabilities.value[sourceId];
    return metadata?.configuration.readiness === 'ready' && metadata.workKinds.includes('capture');
  }

  /** Names a pipeline, adding its section's last path segment when two pipelines share a name. */
  function pipelineLabel(pipeline: PipelineModel) {
    const name = pipeline.name ?? sourceOf(pipeline.sourceId)?.handle ?? pipeline.id.slice(0, 8);
    if (pipeline.scope.url && pipelines.value.filter((item) => item.name === pipeline.name).length > 1) {
      try {
        return name + ' / ' + (new URL(pipeline.scope.url).pathname.split('/').filter(Boolean).pop() ?? 'root');
      } catch {
        return name;
      }
    }
    return name;
  }

  function clearMessages() {
    failure.value = null;
    notice.value = null;
  }

  /** Reads each source's adapter metadata once per container. */
  async function loadCapabilities() {
    const api = client.value;
    const current = containerId.value;
    if (!api || !current) return;
    const ids = [
      ...new Set([...sources.value.map((item) => item.id), ...pipelines.value.map((item) => item.sourceId)]),
    ].filter((id) => !capabilityRequested.has(id));
    if (!ids.length) return;
    for (const id of ids) capabilityRequested.add(id);
    const signal = capabilityRead.signal;
    await Promise.all(
      ids.map(async (id) => {
        const result = await api.getSourceCapabilities(id, signal);
        if (disposed || signal.aborted || client.value !== api || containerId.value !== current) return;
        if (result.ok) {
          capabilities.value = { ...capabilities.value, [id]: result.value };
          capabilityErrors.value = { ...capabilityErrors.value, [id]: '' };
        } else {
          const remaining = { ...capabilities.value };
          delete remaining[id];
          capabilities.value = remaining;
          capabilityErrors.value = {
            ...capabilityErrors.value,
            [id]: 'This runtime could not describe its source capabilities.',
          };
        }
      }),
    );
  }

  /** Loads every pipeline's runs; the API lists runs per pipeline only. */
  async function loadRuns(api: HarvestClient, current: string, signal: AbortSignal) {
    const ids = pipelines.value.map((item) => item.id);
    const results = await Promise.all(ids.map((id) => api.listRuns(id, signal)));
    if (signal.aborted || disposed || client.value !== api || containerId.value !== current) return;
    runsByPipeline.value = Object.fromEntries(
      ids.map((id, index) => {
        const result = results[index];
        return [id, result?.ok ? result.value : []];
      }),
    );
  }

  /** Loads a container snapshot; an aborted or replaced connection cannot overwrite newer data. */
  async function load() {
    const api = client.value;
    const current = containerId.value;
    if (!api || !current) return;
    read?.abort();
    const controller = new AbortController();
    read = controller;
    try {
      const [sourceResult, pipelineResult, postResult, hostResult, subscriptionResult] = await Promise.all([
        api.listSources(current, controller.signal),
        api.listPipelines(current, controller.signal),
        api.listPosts(current, controller.signal),
        api.listHosts(current, controller.signal),
        api.listSubscriptions(controller.signal),
      ]);
      if (controller.signal.aborted || disposed || client.value !== api || containerId.value !== current) return;
      sourceListState.value = sourceResult.ok ? 'ready' : 'unavailable';
      if (sourceResult.ok) sources.value = sourceResult.value;
      if (pipelineResult.ok) pipelines.value = pipelineResult.value;
      if (postResult.ok) posts.value = postResult.value;
      if (hostResult.ok) hosts.value = hostResult.value;
      if (subscriptionResult.ok) subscriptions.value = subscriptionResult.value;
      if (!sourceResult.ok) failure.value = message(sourceResult.failure);
      else if (!pipelineResult.ok) failure.value = message(pipelineResult.failure);
      await loadRuns(api, current, controller.signal);
      if (controller.signal.aborted || containerId.value !== current) return;
      loaded.value = true;
      void loadCapabilities();
    } catch {
      if (!controller.signal.aborted) {
        if (sourceListState.value === 'loading') sourceListState.value = 'unavailable';
        failure.value = 'The console could not refresh. Try again.';
      }
    }
  }

  /** Refreshes the open container; a call during a refresh runs once more after it. */
  function refresh(): Promise<void> {
    if (!client.value || !containerId.value) return Promise.resolve();
    if (inflight) {
      again = true;
      return inflight;
    }
    const current = containerId.value;
    inflight = load().finally(() => {
      inflight = null;
      if (disposed || !client.value) return;
      if (again || containerId.value !== current) {
        again = false;
        void refresh();
      }
    });
    return inflight;
  }

  /** Loads the visible registry before showing the workspace. */
  async function initialize(preferredContainerId?: string) {
    const api = client.value;
    if (!api) return;
    const controller = new AbortController();
    action?.abort();
    action = controller;
    connecting.value = true;
    pending.value = true;
    try {
      const result = await api.listContainers(controller.signal);
      if (disposed || controller.signal.aborted || client.value !== api) return;
      if (!result.ok) {
        failure.value = message(result.failure);
        client.value = null;
        if (result.failure.status === 401 || result.failure.status === 403) clearConnection();
        return;
      }
      containers.value = result.value;
      failure.value = null;
      const tenantResult = await api.listTenants(controller.signal);
      if (disposed || controller.signal.aborted || client.value !== api) return;
      tenants.value = tenantResult.ok ? tenantResult.value : [];
      const chosen = result.value.find((item) => item.id === preferredContainerId) ?? result.value[0];
      containerId.value = chosen?.id ?? '';
      // A new container starts its own refresh; the session shows while that data arrives.
      if (!inflight) await refresh();
    } finally {
      if (!disposed && !controller.signal.aborted) {
        connecting.value = false;
        pending.value = false;
      }
    }
  }

  function saveConnection() {
    if (!connectedApiKey) return;
    try {
      options.connectionStorage?.setItem(
        ConnectionKey,
        JSON.stringify({ apiKey: connectedApiKey, containerId: containerId.value }),
      );
    } catch {
      // A browser can block storage while allowing the current connection.
    }
  }

  function clearConnection() {
    connectedApiKey = '';
    try {
      options.connectionStorage?.removeItem(ConnectionKey);
    } catch {
      // A blocked store cannot restore a connection.
    }
  }

  async function connectWithKey(apiKey: string, preferredContainerId?: string) {
    const current = createHarvestClient({ apiKey });
    client.value = current;
    await initialize(preferredContainerId);
    if (disposed || client.value !== current) return;
    connectedApiKey = apiKey;
    saveConnection();
  }

  function restoreConnection() {
    if (options.connectionManaged || !options.connectionStorage) return;
    try {
      const stored = options.connectionStorage.getItem(ConnectionKey);
      if (!stored) return;
      const session = JSON.parse(stored) as { apiKey?: unknown; containerId?: unknown } | null;
      if (typeof session?.apiKey === 'string' && session.apiKey.trim()) {
        const preferred = typeof session.containerId === 'string' ? session.containerId : undefined;
        void connectWithKey(session.apiKey, preferred);
      }
    } catch {
      clearConnection();
    }
  }

  /** Connects with an API key; only a console that owns its connection takes one. */
  async function connect(apiKey: string) {
    if (options.connectionManaged) return;
    const key = apiKey.trim();
    disconnect();
    await connectWithKey(key);
  }

  /** Cancels requests and removes the current connection and its data. */
  function disconnect() {
    clearConnection();
    capabilityRead.abort();
    capabilityRead = new AbortController();
    capabilityRequested.clear();
    capabilities.value = {};
    capabilityErrors.value = {};
    action?.abort();
    read?.abort();
    client.value = null;
    containers.value = [];
    sources.value = [];
    pipelines.value = [];
    runsByPipeline.value = {};
    posts.value = [];
    hosts.value = [];
    subscriptions.value = [];
    tenants.value = [];
    containerId.value = '';
    loaded.value = false;
    notice.value = null;
    failure.value = null;
    connecting.value = false;
    pending.value = false;
  }

  /** Runs one explicit mutation; repeat clicks cannot issue duplicate requests. Resolves `true` on success. */
  async function mutate<T>(
    operation: (api: HarvestClient, signal: AbortSignal) => Promise<Result<T, ApiFailure>>,
    success: string,
  ): Promise<boolean> {
    if (!client.value || pending.value) return false;
    const api = client.value;
    const controller = new AbortController();
    action?.abort();
    action = controller;
    pending.value = true;
    failure.value = null;
    notice.value = null;
    try {
      const result = await operation(api, controller.signal);
      if (disposed || controller.signal.aborted || client.value !== api) return false;
      if (!result.ok) {
        failure.value = message(result.failure);
        return false;
      }
      notice.value = success;
      await refresh();
      return true;
    } finally {
      if (!disposed && !controller.signal.aborted) pending.value = false;
    }
  }

  /** Creates the shared pool container and opens it. */
  function createPool() {
    return mutate(async (api, signal) => {
      const result = await api.createContainer({ kind: 'pool' }, signal);
      if (result.ok && !signal.aborted) {
        containers.value = [...containers.value, result.value];
        containerId.value = result.value.id;
      }
      return result;
    }, 'Container created.');
  }

  function registerTenant(name: string) {
    return mutate(async (api, signal) => {
      const result = await api.registerTenant({ slug: name, name }, signal);
      if (result.ok && !signal.aborted) tenants.value = [...tenants.value, result.value];
      return result;
    }, 'Tenant registered.');
  }

  /** Declares a pipeline from a source and one of its sections; resolves the new pipeline, or nothing on failure. */
  async function declarePipeline(draft: PipelineDraft): Promise<PipelineModel | undefined> {
    if (!canCapture(draft.sourceId)) return undefined;
    let created: PipelineModel | undefined;
    await mutate(async (api, signal) => {
      const result = await api.declarePipeline(
        {
          containerId: containerId.value,
          sourceId: draft.sourceId,
          name: draft.name?.trim() || undefined,
          scope: draft.sectionUrl ? { url: draft.sectionUrl, restrictToPath: true } : {},
          pinned: true,
          ai: 'off',
          schedules: [
            {
              kind: 'capture',
              mode: 'interval',
              every: `PT${draft.everyMinutes}M`,
              enabled: draft.scheduleEnabled,
              timeZone: 'Asia/Tashkent',
            },
          ],
        },
        signal,
      );
      if (result.ok && !signal.aborted) created = result.value;
      return result;
    }, 'Pipeline created.');
    return created;
  }

  function savePipeline(id: string, update: PipelineUpdate) {
    return mutate((api, signal) => api.updatePipeline(id, update, signal), 'Pipeline settings saved.');
  }

  function startRun(pipelineId: string, kind: WorkKind = 'capture') {
    return mutate((api, signal) => api.startRun(pipelineId, kind, signal), 'Run queued.');
  }

  function cancelRun(id: string) {
    return mutate((api, signal) => api.cancelRun(id, signal), 'Run cancelled.');
  }

  function setPipelinePaused(id: string, paused: boolean) {
    return mutate((api, signal) => api.setPipelinePaused(id, paused, signal), 'Pipeline updated.');
  }

  function subscribe(draft: SubscriptionDraft) {
    const tag = draft.tag?.trim();
    return mutate(
      (api, signal) =>
        api.subscribe(
          {
            tenantId: draft.tenantId,
            containerId: containerId.value,
            pipelineId: draft.pipelineId,
            schema: 'wow2.event.v1',
            ...(tag ? { filterKey: `tag:${tag}` } : {}),
          },
          signal,
        ),
      'Subscription created.',
    );
  }

  /** Pulls a subscription's next delivery page from `cursor`; resolves the page, or nothing on failure. */
  async function pull(subscriptionId: string, cursor?: string): Promise<CursorPage<DeliveryEvent> | undefined> {
    let page: CursorPage<DeliveryEvent> | undefined;
    await mutate(async (api, signal) => {
      const result = await api.pull(subscriptionId, cursor || undefined, signal);
      if (result.ok && !signal.aborted) page = result.value;
      return result;
    }, 'Delivery cursor advanced.');
    return page;
  }

  watch(
    containerId,
    () => {
      saveConnection();
      read?.abort();
      capabilityRead.abort();
      capabilityRead = new AbortController();
      capabilityRequested.clear();
      loaded.value = false;
      sources.value = [];
      sourceListState.value = 'loading';
      pipelines.value = [];
      runsByPipeline.value = {};
      posts.value = [];
      hosts.value = [];
      capabilities.value = {};
      capabilityErrors.value = {};
      void refresh();
    },
    { flush: 'sync' },
  );

  onMounted(() => {
    if (client.value) void initialize();
    else restoreConnection();
    timer = setInterval(() => {
      if (document.visibilityState !== 'hidden') void refresh();
    }, options.refreshIntervalMs ?? DefaultRefreshMs);
  });

  onScopeDispose(() => {
    disposed = true;
    capabilityRead.abort();
    action?.abort();
    read?.abort();
    if (timer) clearInterval(timer);
  });

  return {
    client: client as Readonly<ShallowRef<HarvestClient | null>>,
    connecting: connecting as Readonly<Ref<boolean>>,
    connected,
    connectionManaged,
    hasConnectionStorage: computed(() => options.connectionStorage !== undefined),
    pending: pending as Readonly<Ref<boolean>>,
    failure,
    notice,
    containers: containers as Readonly<ShallowRef<readonly ContainerModel[]>>,
    container,
    containerId,
    loaded: loaded as Readonly<Ref<boolean>>,
    tenants: tenants as Readonly<ShallowRef<readonly TenantModel[]>>,
    sources: sources as Readonly<ShallowRef<readonly SourceModel[]>>,
    sourceListState: sourceListState as Readonly<Ref<ListState>>,
    pipelines: pipelines as Readonly<ShallowRef<readonly PipelineModel[]>>,
    runs,
    posts: posts as Readonly<ShallowRef<readonly PostModel[]>>,
    hosts: hosts as Readonly<ShallowRef<readonly HostHealthModel[]>>,
    subscriptions: visibleSubscriptions,
    capabilities: capabilities as Readonly<ShallowRef<Readonly<Record<string, SourceCapabilitiesModel>>>>,
    capabilityErrors: capabilityErrors as Readonly<ShallowRef<Readonly<Record<string, string>>>>,
    isOperator: isOperator as ComputedRef<boolean | undefined>,
    localWorkerEnabled: localWorkerEnabled as ComputedRef<boolean | undefined>,
    navigation: computed(() => options.navigation),
    can,
    canWrite,
    canCapture,
    sourceOf,
    pipelineOf,
    runsOf,
    sectionsOf,
    pipelineLabel,
    clearMessages,
    refresh,
    connect,
    disconnect,
    createPool,
    registerTenant,
    declarePipeline,
    savePipeline,
    startRun,
    cancelRun,
    setPipelinePaused,
    subscribe,
    pull,
  };
}

/** One console session: the connection, the open container's data and its actions. */
export type HarvestConsole = ReturnType<typeof createHarvestConsole>;

const HarvestConsoleKey: InjectionKey<HarvestConsole> = Symbol('harvest.console');

/** Shares a console session with every panel below the calling component. */
export function provideHarvestConsole(session: HarvestConsole) {
  provide(HarvestConsoleKey, session);
}

/** Reads the console session a `HarvestConsoleProvider` shares. */
export function useHarvestConsole(): HarvestConsole {
  const session = inject(HarvestConsoleKey, null);
  if (!session) throw new Error('SCFD console panels need a HarvestConsoleProvider above them.');
  return session;
}
