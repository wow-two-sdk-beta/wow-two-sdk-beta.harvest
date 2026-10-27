<script setup lang="ts">
import { computed, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
import {
  AppShell,
  AppShellHeader,
  AppShellSidebar,
  AppShellMain,
  AppShellContent,
} from '@wow-two-beta/fe/presentation/layout';
import { SidebarMenu, SidebarMenuItem, SidebarMenuSection } from '@wow-two-beta/fe/presentation/nav';
import {
  Field,
  PasswordInput,
  TextInput,
  NumberInput,
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from '@wow-two-beta/fe/presentation/forms';
import type { ApiFailure } from '@wow-two-beta/fe/foundation/http';
import type { Result } from '@wow-two-beta/fe/foundation/results';
import type {
  ContainerModel,
  SourceModel,
  PipelineModel,
  RunModel,
  PostModel,
  HostHealthModel,
  SubscriptionModel,
  TenantModel,
  DeliveryEvent,
  PipelineUpdate,
} from '../../contracts';
import { createHarvestClient, type HarvestClient } from '../../kit';
import PipelineSettings from './PipelineSettings.vue';
import PostDetailsPanel from './PostDetailsPanel.vue';

/** Reusable pipeline, run, source, post and delivery management for every Harvest capability form. */
defineOptions({ name: 'HarvestConsolePanel' });
const props = defineProps<{ client?: HarvestClient; appVersion?: string; apiVersion?: string }>();
const tabs = ['Pipelines', 'Sources', 'Runs', 'Posts', 'Delivery', 'Hosts'] as const;
const activeTab = ref<(typeof tabs)[number]>('Pipelines');
const sidebarOpen = ref(false);
const sidebarCollapsed = ref(false);
const creatingPipeline = ref(false);
const key = ref('');
const client = shallowRef<HarvestClient | null>(props.client ?? null);
const connected = computed(() => client.value !== null);
const pending = ref(false);
const failure = ref<string | null>(null);
const notice = ref<string | null>(null);
const containers = shallowRef<readonly ContainerModel[]>([]);
const sources = shallowRef<readonly SourceModel[]>([]);
const pipelines = shallowRef<readonly PipelineModel[]>([]);
const runs = shallowRef<readonly RunModel[]>([]);
const posts = shallowRef<readonly PostModel[]>([]);
const hosts = shallowRef<readonly HostHealthModel[]>([]);
const subscriptions = shallowRef<readonly SubscriptionModel[]>([]);
const tenants = shallowRef<readonly TenantModel[]>([]);
const events = shallowRef<readonly DeliveryEvent[]>([]);
const containerId = ref('');
const pipelineId = ref('');
const sourceId = ref('');
const selectedPost = shallowRef<PostModel | null>(null);
const sourceHost = ref('');
const sourceFamily = ref('site');
const pipelineName = ref('');
const startUrl = ref('');
const everyMinutes = ref(60);
const newScheduleEnabled = ref(false);
const tenantId = ref('');
const tenantSlug = ref('');
const subscriptionId = ref('');
const cursor = ref('');
let read: AbortController | null = null;
let action: AbortController | null = null;
let refreshing = false;
let disposed = false;
let timer: ReturnType<typeof setInterval> | null = null;
const containerOptions = computed(() =>
  containers.value.map((item) => ({
    itemKey: item.id,
    value: item.id,
    label: item.externalRef ?? `${item.kind} · ${item.id.slice(0, 8)}`,
  })),
);
const sourceOptions = computed(() =>
  sources.value.map((item) => ({ itemKey: item.id, value: item.id, label: item.handle })),
);
const pipelineOptions = computed(() =>
  pipelines.value.map((item) => ({
    itemKey: item.id,
    value: item.id,
    label: pipelineLabel(item),
  })),
);
const visibleSubscriptions = computed(() =>
  subscriptions.value.filter((item) => pipelines.value.some((pipeline) => pipeline.id === item.pipelineId)),
);
const activeRuns = computed(
  () => runs.value.filter((run) => run.status === 'running' || run.status === 'queued').length,
);
const selectedPipeline = computed(() => pipelines.value.find((item) => item.id === pipelineId.value));

function pipelineLabel(pipeline: PipelineModel) {
  const name =
    pipeline.name ?? sources.value.find((source) => source.id === pipeline.sourceId)?.handle ?? pipeline.id.slice(0, 8);
  if (pipeline.scope.url && pipelines.value.filter((item) => item.name === pipeline.name).length > 1) {
    try {
      return name + ' / ' + (new URL(pipeline.scope.url).pathname.split('/').filter(Boolean).pop() ?? 'root');
    } catch {
      return name;
    }
  }
  return name;
}

function subscriptionLabel(subscription: SubscriptionModel) {
  return pipelines.value.find((item) => item.id === subscription.pipelineId)?.name ?? 'Pipeline subscription';
}
const sectionIcons: Record<(typeof tabs)[number], string> = {
  Pipelines: 'M4 5h6v6H4z M14 13h6v6h-6z M7 11v5h7 M10 8h7v5',
  Sources: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M3 12h18 M12 3c5 5 5 13 0 18 M12 3c-5 5-5 13 0 18',
  Runs: 'm8 4 12 8-12 8z',
  Posts: 'M5 3h14v18H5z M8 8h8 M8 12h8 M8 16h5',
  Delivery: 'M3 5h18v14H3z m0 0 9 8 9-8',
  Hosts: 'M4 3h16v7H4z M4 14h16v7H4z M8 7h.01 M8 18h.01',
};

function openSection(tab: (typeof tabs)[number]) {
  activeTab.value = tab;
  sidebarOpen.value = false;
  creatingPipeline.value = false;
  notice.value = null;
  failure.value = null;
}

function openNavigation() {
  sidebarCollapsed.value = false;
  sidebarOpen.value = !sidebarOpen.value;
}

function openPipeline(id: string) {
  pipelineId.value = id;
  openSection('Pipelines');
}

async function savePipeline(update: PipelineUpdate) {
  const id = pipelineId.value;
  await mutate((api, signal) => api.updatePipeline(id, update, signal), 'Pipeline settings saved.');
}

/** Presents access failures without losing the server's actionable message. */
function message(error: ApiFailure) {
  return error.status === 401 ? 'An API key is needed to read the registry.' : error.message;
}

/** Loads a container snapshot; an aborted or replaced connection cannot overwrite newer data. */
async function refresh() {
  const currentClient = client.value;
  const currentContainer = containerId.value;
  if (!currentClient || !currentContainer || refreshing) return;
  refreshing = true;
  read?.abort();
  const current = new AbortController();
  read = current;
  try {
    const [sourceResult, pipelineResult, postResult, hostResult, subscriptionResult] = await Promise.all([
      currentClient.listSources(currentContainer, current.signal),
      currentClient.listPipelines(currentContainer, current.signal),
      currentClient.listPosts(currentContainer, current.signal),
      currentClient.listHosts(currentContainer, current.signal),
      currentClient.listSubscriptions(current.signal),
    ]);
    if (current.signal.aborted || disposed || client.value !== currentClient || containerId.value !== currentContainer)
      return;
    if (sourceResult.ok) sources.value = sourceResult.value;
    if (pipelineResult.ok) {
      pipelines.value = pipelineResult.value;
      if (!pipelines.value.some((item) => item.id === pipelineId.value))
        pipelineId.value = pipelines.value[0]?.id ?? '';
    }
    if (postResult.ok) {
      posts.value = postResult.value;
      if (selectedPost.value)
        selectedPost.value = posts.value.find((post) => post.id === selectedPost.value?.id) ?? null;
    }
    if (hostResult.ok) hosts.value = hostResult.value;
    if (subscriptionResult.ok) subscriptions.value = subscriptionResult.value;
    if (!sourceResult.ok) failure.value = message(sourceResult.failure);
    else if (!pipelineResult.ok) failure.value = message(pipelineResult.failure);
    if (!sourceId.value || !sources.value.some((item) => item.id === sourceId.value))
      sourceId.value = sources.value[0]?.id ?? '';
    await loadRuns(current.signal);
  } catch {
    if (!current.signal.aborted) failure.value = 'The console could not refresh. Try again.';
  } finally {
    refreshing = false;
    if (!disposed && client.value && containerId.value !== currentContainer) void refresh();
  }
}

/** Refreshes runs for the selected pipeline, retaining its scope while awaiting the server. */
async function loadRuns(signal?: AbortSignal) {
  const currentClient = client.value;
  const currentPipeline = pipelineId.value;
  if (!currentClient || !currentPipeline) {
    runs.value = [];
    return;
  }
  const result = await currentClient.listRuns(currentPipeline, signal);
  if (!signal?.aborted && !disposed && client.value === currentClient && pipelineId.value === currentPipeline)
    runs.value = result.ok ? result.value : [];
}

/** Loads the visible registry before selecting a container. */
async function initialize() {
  const currentClient = client.value;
  if (!currentClient) return;
  const current = new AbortController();
  action?.abort();
  action = current;
  pending.value = true;
  try {
    const result = await currentClient.listContainers(current.signal);
    if (disposed || current.signal.aborted || client.value !== currentClient) return;
    if (!result.ok) {
      failure.value = message(result.failure);
      client.value = null;
      return;
    }
    containers.value = result.value;
    failure.value = null;
    const tenantResult = await currentClient.listTenants(current.signal);
    if (disposed || current.signal.aborted || client.value !== currentClient) return;
    tenants.value = tenantResult.ok ? tenantResult.value : [];
    containerId.value = result.value[0]?.id ?? '';
    tenantId.value = result.value[0]?.tenantId ?? tenants.value[0]?.id ?? '';
    await refresh();
  } finally {
    if (!disposed && !current.signal.aborted) pending.value = false;
  }
}

/** Connects with a key held in memory for this page only. */
async function connect() {
  disconnect();
  client.value = createHarvestClient({ apiKey: key.value.trim() });
  key.value = '';
  await initialize();
}

/** Cancels requests and removes the current connection and its data. */
function disconnect() {
  action?.abort();
  read?.abort();
  client.value = null;
  containers.value = [];
  sources.value = [];
  pipelines.value = [];
  runs.value = [];
  posts.value = [];
  hosts.value = [];
  subscriptions.value = [];
  tenants.value = [];
  events.value = [];
  containerId.value = '';
  pipelineId.value = '';
  sourceId.value = '';
  selectedPost.value = null;
  subscriptionId.value = '';
  cursor.value = '';
  notice.value = null;
  failure.value = null;
  pending.value = false;
}

/** Runs one explicit mutation; repeat clicks cannot issue duplicate requests. */
async function mutate<T>(
  operation: (api: HarvestClient, signal: AbortSignal) => Promise<Result<T, ApiFailure>>,
  success: string,
) {
  if (!client.value || pending.value) return;
  const currentClient = client.value;
  const current = new AbortController();
  action?.abort();
  action = current;
  pending.value = true;
  failure.value = null;
  notice.value = null;
  try {
    const result = await operation(currentClient, current.signal);
    if (disposed || current.signal.aborted || client.value !== currentClient) return;
    if (!result.ok) {
      failure.value = message(result.failure);
      return;
    }
    notice.value = success;
    await refresh();
  } finally {
    if (!disposed && !current.signal.aborted) pending.value = false;
  }
}

async function createPool() {
  await mutate(async (api, signal) => {
    const result = await api.createContainer({ kind: 'pool' }, signal);
    if (result.ok && !signal.aborted) {
      containers.value = [...containers.value, result.value];
      containerId.value = result.value.id;
    }
    return result;
  }, 'Container created.');
}

async function registerSource() {
  await mutate(
    (api, signal) =>
      api.registerSource(
        {
          containerId: containerId.value,
          family: sourceFamily.value,
          handle: sourceHost.value,
          coverage:
            sourceFamily.value === 'instagram' || sourceFamily.value === 'listing'
              ? 'filtered'
              : sourceFamily.value === 'site'
                ? 'whole'
                : 'single',
          completeness:
            sourceFamily.value === 'instagram' || sourceFamily.value === 'listing'
              ? 'window'
              : sourceFamily.value === 'site'
                ? 'complete'
                : 'none',
          posture: sourceFamily.value === 'instagram' ? 'official' : 'ownFetch',
          checkPolicy: sourceFamily.value === 'instagram' ? 'none' : 'web-direct',
          robotsPolicy: 'obey',
          aiAllowed: false,
        },
        signal,
      ),
    'Source registered.',
  );
}

async function declarePipeline() {
  await mutate(async (api, signal) => {
    const result = await api.declarePipeline(
      {
        containerId: containerId.value,
        sourceId: sourceId.value,
        name: pipelineName.value || undefined,
        scope: startUrl.value.trim() ? { url: startUrl.value.trim() } : {},
        pinned: true,
        ai: 'off',
        schedules: [
          {
            kind: 'capture',
            mode: 'interval',
            every: `PT${everyMinutes.value}M`,
            enabled: newScheduleEnabled.value,
            timeZone: 'Asia/Tashkent',
          },
        ],
      },
      signal,
    );
    if (result.ok && !signal.aborted) {
      pipelineId.value = result.value.id;
      creatingPipeline.value = false;
      pipelineName.value = '';
      startUrl.value = '';
    }
    return result;
  }, 'Pipeline created.');
}

async function addTenant() {
  await mutate(async (api, signal) => {
    const result = await api.registerTenant({ slug: tenantSlug.value, name: tenantSlug.value }, signal);
    if (result.ok && !signal.aborted) {
      tenants.value = [...tenants.value, result.value];
      tenantId.value = result.value.id;
      tenantSlug.value = '';
    }
    return result;
  }, 'Tenant registered.');
}

async function subscribe() {
  await mutate(
    (api, signal) =>
      api.subscribe(
        {
          tenantId: tenantId.value,
          containerId: containerId.value,
          pipelineId: pipelineId.value,
          schema: 'wow2.event.v1',
        },
        signal,
      ),
    'Subscription created.',
  );
}

async function pull(subscription: SubscriptionModel) {
  const previous = subscriptionId.value === subscription.id ? cursor.value : '';
  await mutate(async (api, signal) => {
    const result = await api.pull(subscription.id, previous || undefined, signal);
    if (result.ok && !signal.aborted) {
      subscriptionId.value = subscription.id;
      cursor.value = result.value.next;
      events.value = result.value.data;
    }
    return result;
  }, 'Delivery cursor advanced.');
}

function date(value?: string) {
  return value ? new Date(value).toLocaleString() : '—';
}

watch(
  containerId,
  () => {
    read?.abort();
    selectedPost.value = null;
    sources.value = [];
    pipelines.value = [];
    runs.value = [];
    posts.value = [];
    hosts.value = [];
    pipelineId.value = '';
    sourceId.value = '';
    events.value = [];
    subscriptionId.value = '';
    cursor.value = '';
    tenantId.value =
      containers.value.find((item) => item.id === containerId.value)?.tenantId ?? tenants.value[0]?.id ?? '';
    void refresh();
  },
  { flush: 'sync' },
);
watch(pipelineId, () => {
  void loadRuns();
});
onMounted(() => {
  if (client.value) void initialize();
  timer = setInterval(() => {
    if (document.visibilityState !== 'hidden') void refresh();
  }, 5000);
});
onScopeDispose(() => {
  disposed = true;
  action?.abort();
  read?.abort();
  if (timer) clearInterval(timer);
});
</script>

<template>
  <AppShell
    v-model:sidebar-open="sidebarOpen"
    class="surface-ambient font-sans text-foreground"
    sidebar-breakpoint="md"
    :sidebar-width="sidebarCollapsed ? '64px' : '248px'"
  >
    <AppShellHeader variant="glass" height="auto" class="z-20 border-b border-border">
      <div class="flex min-h-16 w-full items-center gap-3 px-4">
        <Button
          v-if="connected"
          size="sm"
          variant="ghost"
          class="md:hidden"
          aria-label="Open navigation"
          @click="openNavigation"
        >
          ☰
        </Button>
        <a href="/" class="flex items-center gap-3 text-lg font-semibold tracking-tight md:w-[204px] md:shrink-0">
          <span class="grid size-8 place-items-center rounded-xl bg-primary/10 text-primary" aria-hidden="true">H</span>
          Harvest
        </a>
        <span v-if="connected" class="hidden min-w-0 flex-1 truncate text-sm text-muted-foreground sm:block">
          {{ activeTab }}
          <span v-if="activeTab === 'Pipelines' && selectedPipeline && !creatingPipeline">
            /
            <span class="text-foreground">{{ selectedPipeline.name ?? 'Pipeline' }}</span>
          </span>
        </span>
        <span v-else class="flex-1"></span>
        <span v-if="appVersion" aria-label="App version" class="hidden lg:inline-flex" :title="appVersion">
          <Badge>app {{ appVersion.split('+')[0] }}</Badge>
        </span>
        <span v-if="apiVersion" aria-label="API version" class="hidden lg:inline-flex" :title="apiVersion">
          <Badge>api {{ apiVersion.split('+')[0] }}</Badge>
        </span>
        <Button
          v-if="connected"
          size="sm"
          variant="ghost"
          class="hidden sm:inline-flex"
          :is-loading="pending"
          @click="refresh"
        >
          Refresh
        </Button>
        <Button v-if="connected" size="sm" variant="outline" @click="disconnect">Disconnect</Button>
      </div>
    </AppShellHeader>
    <AppShellSidebar v-if="connected" class="border-r border-border bg-transparent [&>nav]:p-0">
      <div class="flex h-full min-h-0 flex-col">
        <div v-if="!sidebarCollapsed" class="border-b border-border px-4 py-5">
          <p class="mb-2 text-[10px] font-semibold uppercase tracking-[0.18em] text-muted-foreground">Workspace</p>
          <Field label="Container">
            <SelectPicker v-model="containerId" :options="containerOptions">
              <SelectPickerTrigger><SelectPickerValue placeholder="Choose a container" /></SelectPickerTrigger>
              <SelectPickerContent>
                <SelectPickerItem v-for="option in containerOptions" :key="option.itemKey" v-bind="option" />
              </SelectPickerContent>
            </SelectPicker>
          </Field>
        </div>
        <nav aria-label="Harvest sections" class="min-h-0 flex-1 overflow-y-auto">
          <SidebarMenu :is-collapsed="sidebarCollapsed" class="p-3">
            <SidebarMenuItem v-for="tab in tabs" :key="tab" as-child :is-active="activeTab === tab">
              <template #icon>
                <svg
                  class="size-4 shrink-0"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="1.5"
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  aria-hidden="true"
                >
                  <path :d="sectionIcons[tab]" />
                </svg>
              </template>
              <button type="button" :title="tab" @click="openSection(tab)">
                <span :class="sidebarCollapsed ? 'sr-only' : 'min-w-0 flex-1 truncate text-left'">{{ tab }}</span>
              </button>
            </SidebarMenuItem>
            <SidebarMenuSection v-if="!sidebarCollapsed" label="Pipelines">
              <SidebarMenuItem
                v-for="pipeline in pipelines"
                :key="pipeline.id"
                as-child
                :is-active="activeTab === 'Pipelines' && pipelineId === pipeline.id && !creatingPipeline"
              >
                <template #icon>
                  <span
                    class="size-2 rounded-full"
                    :class="pipeline.state === 'active' ? 'bg-primary/70' : 'bg-muted-foreground/50'"
                  ></span>
                </template>
                <button
                  type="button"
                  class="text-left"
                  :title="pipeline.name ?? pipeline.id"
                  @click="openPipeline(pipeline.id)"
                >
                  <span class="min-w-0 flex-1 truncate">{{ pipelineLabel(pipeline) }}</span>
                </button>
              </SidebarMenuItem>
              <Button
                size="sm"
                variant="ghost"
                class="mt-2 w-full justify-start text-muted-foreground"
                @click="
                  activeTab = 'Pipelines';
                  creatingPipeline = true;
                  sidebarOpen = false;
                "
              >
                + New pipeline
              </Button>
            </SidebarMenuSection>
          </SidebarMenu>
        </nav>
        <div class="border-t border-border p-3">
          <p v-if="!sidebarCollapsed" class="mb-3 px-2 text-[10px] text-muted-foreground">
            Source operations
            <span v-if="appVersion">· {{ appVersion.split('+')[0] }}</span>
          </p>
          <Button
            size="sm"
            variant="ghost"
            class="w-full"
            :aria-label="sidebarCollapsed ? 'Expand sidebar' : 'Collapse sidebar'"
            @click="sidebarCollapsed = !sidebarCollapsed"
          >
            {{ sidebarCollapsed ? '→' : '← Collapse' }}
          </Button>
        </div>
      </div>
    </AppShellSidebar>
    <AppShellMain>
      <AppShellContent gutter="lg" max-width="2xl">
        <div class="space-y-6 py-3">
          <Card v-if="!connected" variant="glass" class="mx-auto max-w-xl p-7 sm:p-9">
            <p class="text-xs font-semibold uppercase tracking-[0.2em] text-primary">Your sources, on schedule</p>
            <h2 class="mt-3 text-2xl font-semibold">Connect to Harvest</h2>
            <p class="mt-2 text-sm leading-relaxed text-muted-foreground">
              Bring your pipelines, captures and deliveries into one place.
            </p>
            <form class="mt-7 space-y-4" @submit.prevent="connect">
              <label class="block space-y-2 text-sm">
                <span>API key</span>
                <PasswordInput
                  v-model="key"
                  autocomplete="off"
                  placeholder="hv_…"
                  :has-toggle="false"
                  :is-required="true"
                />
              </label>
              <Button type="submit" class="w-full" :is-loading="pending">Connect</Button>
            </form>
            <p class="mt-4 text-xs text-muted-foreground">
              The key stays in memory until you disconnect or close this page.
            </p>
          </Card>
          <p
            v-if="failure"
            class="rounded-xl border border-destructive/25 bg-card p-4 text-sm text-destructive"
            role="alert"
          >
            {{ failure }}
          </p>
          <p v-if="notice" class="text-sm text-primary" role="status">{{ notice }}</p>

          <template v-if="connected">
            <div class="flex flex-wrap items-end justify-between gap-4">
              <div>
                <p class="text-[10px] font-semibold uppercase tracking-[0.2em] text-primary">Source operations</p>
                <h1 class="mt-2 text-2xl font-semibold tracking-tight">
                  {{
                    activeTab === 'Pipelines'
                      ? creatingPipeline
                        ? 'New pipeline'
                        : (selectedPipeline?.name ?? 'Pipelines')
                      : activeTab
                  }}
                </h1>
                <p class="mt-2 text-sm text-muted-foreground">
                  {{
                    activeTab === 'Pipelines'
                      ? 'Choose a saved target. Set its cadence. Follow each run.'
                      : 'Manage your harvest workspace.'
                  }}
                </p>
              </div>
              <Button
                v-if="activeTab === 'Pipelines' && !creatingPipeline"
                variant="outline"
                @click="creatingPipeline = true"
              >
                New pipeline
              </Button>
            </div>
            <Card v-if="!containers.length" variant="glass" class="p-8">
              <h2 class="text-lg font-semibold">Start with a container</h2>
              <p class="mt-2 text-sm text-muted-foreground">
                A shared container holds sources and posts that your tenants can subscribe to.
              </p>
              <Button class="mt-5" :is-loading="pending" @click="createPool">Create shared container</Button>
            </Card>
            <template v-else>
              <div class="grid grid-cols-2 gap-3 lg:grid-cols-4">
                <Card variant="glass" class="p-5">
                  <p class="text-xs text-muted-foreground">Pipelines</p>
                  <p class="mt-2 text-3xl font-semibold">{{ pipelines.length }}</p>
                </Card>
                <Card variant="glass" class="p-5">
                  <p class="text-xs text-muted-foreground">Recent posts</p>
                  <p class="mt-2 text-3xl font-semibold">{{ posts.length }}{{ posts.length >= 100 ? '+' : '' }}</p>
                </Card>
                <Card variant="glass" class="p-5">
                  <p class="text-xs text-muted-foreground">Selected pipeline · active runs</p>
                  <p class="mt-2 text-3xl font-semibold">{{ activeRuns }}</p>
                </Card>
                <Card variant="glass" class="p-5">
                  <p class="text-xs text-muted-foreground">Queued work</p>
                  <p class="mt-2 text-3xl font-semibold">{{ hosts.reduce((total, host) => total + host.queued, 0) }}</p>
                </Card>
              </div>
              <div v-if="activeTab === 'Pipelines'" class="grid gap-6">
                <Card v-if="!creatingPipeline" variant="glass" class="p-6">
                  <h2 class="text-lg font-semibold">Pipeline configuration</h2>
                  <p class="mt-1 text-sm text-muted-foreground">
                    A standing harvest of a source, with its own saved target and schedule.
                  </p>
                  <ul class="mt-6 divide-y divide-border" aria-label="Pipeline overview">
                    <li
                      v-for="pipeline in pipelines.filter((item) => item.id === pipelineId)"
                      :key="pipeline.id"
                      class="space-y-3 pb-5"
                    >
                      <div class="flex items-center justify-between gap-3">
                        <div>
                          <p class="font-medium">
                            {{ pipeline.name ?? sources.find((source) => source.id === pipeline.sourceId)?.handle }}
                          </p>
                          <p class="mt-1 break-all text-xs text-muted-foreground">
                            {{ pipeline.scope.url ?? 'Whole source' }}
                          </p>
                        </div>
                        <Badge>{{ pipeline.state }}</Badge>
                      </div>
                      <p class="text-xs text-muted-foreground">
                        {{
                          pipeline.schedules
                            .map(
                              (schedule) =>
                                `${schedule.kind} · ${schedule.enabled && schedule.mode !== 'manual' ? (schedule.cron ?? schedule.every ?? schedule.mode) : 'Manual only'}`,
                            )
                            .join(' / ') || 'Manual runs only'
                        }}
                      </p>
                      <div class="flex gap-2">
                        <Button
                          size="sm"
                          :is-disabled="pending || pipeline.state !== 'active'"
                          @click="mutate((api, signal) => api.startRun(pipeline.id, 'capture', signal), 'Run queued.')"
                        >
                          Run now
                        </Button>
                        <Button
                          size="sm"
                          variant="outline"
                          :is-disabled="pending"
                          @click="
                            mutate(
                              (api, signal) => api.setPipelinePaused(pipeline.id, pipeline.state !== 'paused', signal),
                              'Pipeline updated.',
                            )
                          "
                        >
                          {{ pipeline.state === 'paused' ? 'Resume' : 'Pause' }}
                        </Button>
                      </div>
                    </li>
                  </ul>
                  <PipelineSettings
                    v-if="selectedPipeline"
                    :key="selectedPipeline.id"
                    class="mt-6 border-t border-border pt-6"
                    :pipeline="selectedPipeline"
                    :pending="pending"
                    @save="savePipeline"
                  />
                  <p v-if="!pipelines.length" class="mt-8 text-sm text-muted-foreground">
                    No pipelines yet. Add a source, then schedule its first harvest.
                  </p>
                </Card>
                <Card v-if="creatingPipeline || !pipelines.length" variant="glass" class="max-w-2xl self-start p-6">
                  <h3 class="font-semibold">Create a pipeline</h3>
                  <form class="mt-5 space-y-4" @submit.prevent="declarePipeline">
                    <div class="space-y-2">
                      <Field label="Pipeline source">
                        <SelectPicker v-model="sourceId" :options="sourceOptions">
                          <SelectPickerTrigger><SelectPickerValue placeholder="Choose a source" /></SelectPickerTrigger>
                          <SelectPickerContent>
                            <SelectPickerItem v-for="option in sourceOptions" :key="option.itemKey" v-bind="option" />
                          </SelectPickerContent>
                        </SelectPicker>
                      </Field>
                    </div>
                    <label class="block space-y-2 text-sm">
                      <span>Name</span>
                      <TextInput v-model="pipelineName" placeholder="Upcoming events" />
                    </label>
                    <label class="block space-y-2 text-sm">
                      <span>Starting URL / saved filter</span>
                      <TextInput v-model="startUrl" placeholder="Paste the full filtered URL or account URL" />
                    </label>
                    <label class="block space-y-2 text-sm">
                      <span>Run every (minutes)</span>
                      <NumberInput v-model="everyMinutes" :min="5" :max="525600" />
                    </label>
                    <Button
                      type="button"
                      size="sm"
                      variant="outline"
                      :aria-pressed="newScheduleEnabled"
                      @click="newScheduleEnabled = !newScheduleEnabled"
                    >
                      {{ newScheduleEnabled ? 'Scheduled capture enabled' : 'Manual capture only' }}
                    </Button>
                    <Button type="submit" :is-disabled="!sourceId" :is-loading="pending">Create pipeline</Button>
                    <Button v-if="pipelines.length" type="button" variant="ghost" @click="creatingPipeline = false">
                      Cancel
                    </Button>
                  </form>
                </Card>
              </div>

              <div v-else-if="activeTab === 'Sources'" class="grid gap-6 xl:grid-cols-[1fr_22rem]">
                <Card variant="glass" class="p-6">
                  <h2 class="text-lg font-semibold">Sources</h2>
                  <ul class="mt-5 divide-y divide-border" aria-label="Sources">
                    <li v-for="source in sources" :key="source.id" class="flex justify-between gap-4 py-4">
                      <div>
                        <p class="font-medium">{{ source.handle }}</p>
                        <p class="mt-1 text-xs text-muted-foreground">{{ source.family }} · {{ source.coverage }}</p>
                      </div>
                      <Badge>{{ source.state }}</Badge>
                    </li>
                  </ul>
                  <p v-if="!sources.length" class="mt-6 text-sm text-muted-foreground">No sources registered.</p>
                </Card>
                <Card variant="glass" class="self-start p-6">
                  <h3 class="font-semibold">Register a source</h3>
                  <form class="mt-5 space-y-4" @submit.prevent="registerSource">
                    <label class="block space-y-2 text-sm">
                      <span>Host</span>
                      <TextInput v-model="sourceHost" placeholder="example.org" :is-required="true" />
                    </label>
                    <div class="space-y-2">
                      <Field label="Source family">
                        <SelectPicker v-model="sourceFamily">
                          <SelectPickerTrigger><SelectPickerValue /></SelectPickerTrigger>
                          <SelectPickerContent>
                            <SelectPickerItem item-key="site" label="Website / sitemap" />
                            <SelectPickerItem item-key="structured" label="Structured event page" />
                            <SelectPickerItem item-key="feed" label="RSS / ICS feed" />
                            <SelectPickerItem item-key="page" label="Web page" />
                            <SelectPickerItem item-key="listing" label="Marketplace / filtered listing" />
                            <SelectPickerItem item-key="instagram" label="Instagram / official API" />
                          </SelectPickerContent>
                        </SelectPicker>
                      </Field>
                    </div>
                    <p class="text-xs leading-relaxed text-muted-foreground">
                      Register the host (for example www.instagram.com), then save a profile or filtered URL in a
                      pipeline. Collection requires a matching source adapter and its configuration.
                    </p>
                    <Button type="submit" :is-loading="pending">Register source</Button>
                  </form>
                </Card>
              </div>

              <Card v-else-if="activeTab === 'Runs'" variant="glass" class="p-6">
                <div class="flex flex-wrap justify-between gap-4">
                  <div>
                    <h2 class="text-lg font-semibold">Runs</h2>
                    <p class="mt-1 text-sm text-muted-foreground">Latest executions and their outcomes.</p>
                  </div>
                  <div class="w-64">
                    <Field label="Run pipeline">
                      <SelectPicker v-model="pipelineId" :options="pipelineOptions">
                        <SelectPickerTrigger><SelectPickerValue placeholder="Choose a pipeline" /></SelectPickerTrigger>
                        <SelectPickerContent>
                          <SelectPickerItem v-for="option in pipelineOptions" :key="option.itemKey" v-bind="option" />
                        </SelectPickerContent>
                      </SelectPicker>
                    </Field>
                  </div>
                </div>
                <ul class="mt-6 divide-y divide-border" aria-label="Runs">
                  <li v-for="run in runs" :key="run.id" class="space-y-3 py-4">
                    <div class="flex items-center justify-between gap-4">
                      <div>
                        <Badge>{{ run.status }}</Badge>
                        <span class="ml-3 text-sm">{{ run.kind }}</span>
                        <p class="mt-2 text-xs text-muted-foreground">{{ date(run.queuedAtUtc) }}</p>
                      </div>
                      <Button
                        v-if="run.status === 'queued' || run.status === 'running'"
                        size="sm"
                        variant="outline"
                        :is-disabled="pending"
                        @click="mutate((api, signal) => api.cancelRun(run.id, signal), 'Run cancelled.')"
                      >
                        Cancel
                      </Button>
                    </div>
                    <p class="text-sm">
                      {{ run.captured }} captured · {{ run.unchanged }} unchanged · {{ run.discovered }} discovered ·
                      {{ run.failed }} failed
                    </p>
                    <p v-if="run.fetchOutcome || run.message" class="text-sm text-muted-foreground">
                      {{ run.fetchOutcome }} {{ run.message }}
                    </p>
                  </li>
                </ul>
                <p v-if="!runs.length" class="mt-8 text-sm text-muted-foreground">
                  No runs yet. Start a pipeline to see its progress.
                </p>
              </Card>

              <div v-else-if="activeTab === 'Posts'" class="grid gap-6 lg:grid-cols-2">
                <Card variant="glass" class="p-6">
                  <h2 class="text-lg font-semibold">Posts</h2>
                  <ul class="mt-5 divide-y divide-border" aria-label="Posts">
                    <li v-for="post in posts" :key="post.id" class="py-3">
                      <button
                        class="flex w-full items-center justify-between gap-3 rounded-lg p-2 text-left focus-visible:outline-2 focus-visible:outline-primary"
                        @click="selectedPost = post"
                      >
                        <div>
                          <p class="font-medium">{{ post.base.title ?? 'Untitled post' }}</p>
                          <p class="mt-1 text-xs text-muted-foreground">
                            {{ post.base.kind }} · revision {{ post.revision }} · {{ post.links.length }} source links
                          </p>
                        </div>
                        <Badge>{{ post.state }}</Badge>
                      </button>
                    </li>
                  </ul>
                  <p v-if="!posts.length" class="mt-7 text-sm text-muted-foreground">
                    Captured posts appear here after a successful run.
                  </p>
                </Card>
                <Card variant="glass" class="self-start p-6">
                  <PostDetailsPanel v-if="selectedPost" :post="selectedPost" :pipelines="pipelines" />
                  <p v-else class="text-sm text-muted-foreground">
                    Select a post to inspect its source links and field evidence.
                  </p>
                </Card>
              </div>

              <div v-else-if="activeTab === 'Delivery'" class="grid gap-6 xl:grid-cols-[1fr_22rem]">
                <Card variant="glass" class="p-6">
                  <h2 class="text-lg font-semibold">Subscriptions</h2>
                  <ul class="mt-5 divide-y divide-border" aria-label="Subscriptions">
                    <li
                      v-for="subscription in visibleSubscriptions"
                      :key="subscription.id"
                      class="flex flex-wrap justify-between gap-4 py-4"
                    >
                      <div>
                        <p class="font-medium">
                          {{ subscriptionLabel(subscription) }}
                        </p>
                        <p class="mt-1 text-xs text-muted-foreground">
                          {{ subscription.schema ?? 'Post' }} · {{ subscription.state }}
                          <span v-if="subscription.filterKey">· {{ subscription.filterKey }}</span>
                        </p>
                      </div>
                      <Button size="sm" variant="outline" :is-disabled="pending" @click="pull(subscription)">
                        Pull next events
                      </Button>
                    </li>
                  </ul>
                  <p v-if="!visibleSubscriptions.length" class="mt-7 text-sm text-muted-foreground">
                    No subscriptions for this container.
                  </p>
                  <div v-if="subscriptionId" class="mt-6 border-t border-border pt-5">
                    <h3 class="text-sm font-semibold">Latest delivery page</h3>
                    <ul class="mt-3 space-y-2 text-sm">
                      <li v-for="event in events" :key="event.eventId">
                        {{ event.type }} · revision {{ event.revision ?? '—' }} · {{ date(event.occurredAtUtc) }}
                      </li>
                    </ul>
                    <p v-if="!events.length" class="mt-3 text-sm text-muted-foreground">
                      No new events. This cursor will resume from the same position.
                    </p>
                  </div>
                </Card>
                <div class="space-y-6">
                  <Card variant="glass" class="p-6">
                    <h3 class="font-semibold">Subscribe to a pipeline</h3>
                    <form class="mt-5 space-y-4" @submit.prevent="subscribe">
                      <label class="block space-y-2 text-sm">
                        <span>Tenant ID</span>
                        <TextInput v-model="tenantId" :is-required="true" />
                      </label>
                      <div class="space-y-2">
                        <Field label="Subscription pipeline">
                          <SelectPicker v-model="pipelineId" :options="pipelineOptions">
                            <SelectPickerTrigger>
                              <SelectPickerValue placeholder="Choose a pipeline" />
                            </SelectPickerTrigger>
                            <SelectPickerContent>
                              <SelectPickerItem
                                v-for="option in pipelineOptions"
                                :key="option.itemKey"
                                v-bind="option"
                              />
                            </SelectPickerContent>
                          </SelectPicker>
                        </Field>
                      </div>
                      <Button type="submit" :is-disabled="!pipelineId || !tenantId" :is-loading="pending">
                        Subscribe
                      </Button>
                    </form>
                  </Card>
                  <Card variant="glass" class="p-6">
                    <h3 class="font-semibold">Register a tenant</h3>
                    <form class="mt-4 space-y-4" @submit.prevent="addTenant">
                      <label class="block space-y-2 text-sm">
                        <span>Name</span>
                        <TextInput v-model="tenantSlug" placeholder="events" :is-required="true" />
                      </label>
                      <Button type="submit" variant="outline" :is-loading="pending">Register tenant</Button>
                    </form>
                  </Card>
                </div>
              </div>

              <Card v-else variant="glass" class="p-6">
                <h2 class="text-lg font-semibold">Host health</h2>
                <p class="mt-1 text-sm text-muted-foreground">Queue depth and pauses update every five seconds.</p>
                <ul class="mt-5 divide-y divide-border" aria-label="Host health">
                  <li v-for="host in hosts" :key="`${host.hostKey}:${host.lane}`" class="space-y-2 py-4">
                    <div class="flex justify-between gap-3">
                      <p class="font-medium">{{ host.hostKey }}</p>
                      <Badge>
                        {{ host.pausedUntilUtc && new Date(host.pausedUntilUtc) > new Date() ? 'Waiting' : 'Ready' }}
                      </Badge>
                    </div>
                    <p class="text-sm text-muted-foreground">
                      {{ host.queued }} queued · {{ host.running }} running · {{ host.consecutiveFailures }} consecutive
                      failures
                    </p>
                    <p v-if="host.pausedUntilUtc" class="text-xs text-muted-foreground">
                      Paused until {{ date(host.pausedUntilUtc) }} {{ host.lastChallengeKind }}
                    </p>
                  </li>
                </ul>
                <p v-if="!hosts.length" class="mt-7 text-sm text-muted-foreground">
                  Host lanes appear when work is queued.
                </p>
              </Card>
            </template>
          </template>
        </div>
      </AppShellContent>
    </AppShellMain>
  </AppShell>
</template>
