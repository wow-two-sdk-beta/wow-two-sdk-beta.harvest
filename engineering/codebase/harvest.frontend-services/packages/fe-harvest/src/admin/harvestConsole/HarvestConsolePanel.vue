<script setup lang="ts">
import { computed, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, Card } from '@wow-two-beta/fe/presentation/display';
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
} from '../../contracts';
import { createHarvestClient, type HarvestClient } from '../../kit';

/** Reusable pipeline, run, source, post and delivery management for every Harvest capability form. */
defineOptions({ name: 'HarvestConsolePanel' });
const props = defineProps<{ client?: HarvestClient }>();
const tabs = ['Pipelines', 'Sources', 'Runs', 'Posts', 'Delivery', 'Hosts'] as const;
const activeTab = ref<(typeof tabs)[number]>('Pipelines');
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
    label: item.name ?? sources.value.find((source) => source.id === item.sourceId)?.handle ?? item.id.slice(0, 8),
  })),
);
const visibleSubscriptions = computed(() =>
  subscriptions.value.filter((item) => pipelines.value.some((pipeline) => pipeline.id === item.pipelineId)),
);
const activeRuns = computed(
  () => runs.value.filter((run) => run.status === 'running' || run.status === 'queued').length,
);

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
    if (postResult.ok) posts.value = postResult.value;
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
          coverage: sourceFamily.value === 'site' ? 'whole' : 'single',
          completeness: sourceFamily.value === 'site' ? 'complete' : 'none',
          posture: 'ownFetch',
          robotsPolicy: 'obey',
          aiAllowed: false,
        },
        signal,
      ),
    'Source registered.',
  );
}

async function declarePipeline() {
  await mutate(
    (api, signal) =>
      api.declarePipeline(
        {
          containerId: containerId.value,
          sourceId: sourceId.value,
          name: pipelineName.value || undefined,
          scope: startUrl.value.trim() ? { url: startUrl.value.trim() } : {},
          pinned: true,
          ai: 'off',
          schedules: [
            { kind: 'capture', mode: 'interval', every: `PT${everyMinutes.value}M` },
            { kind: 'check', mode: 'interval', every: 'PT6H' },
          ],
        },
        signal,
      ),
    'Pipeline scheduled.',
  );
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

/** Rejects non-web link schemes at the presentation boundary. */
function webUrl(url: string) {
  try {
    const parsed = new URL(url);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? parsed.href : undefined;
  } catch {
    return undefined;
  }
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
  <div class="space-y-6">
    <Card v-if="!connected" variant="glass" class="mx-auto max-w-xl p-7 sm:p-9">
      <p class="text-xs font-semibold uppercase tracking-[0.2em] text-primary">Your sources, on schedule</p>
      <h2 class="mt-3 text-2xl font-semibold">Connect to Harvest</h2>
      <p class="mt-2 text-sm leading-relaxed text-muted-foreground">
        Bring your pipelines, captures and deliveries into one place.
      </p>
      <form class="mt-7 space-y-4" @submit.prevent="connect">
        <label class="block space-y-2 text-sm">
          <span>API key</span>
          <PasswordInput v-model="key" autocomplete="off" placeholder="hv_…" :has-toggle="false" :is-required="true" />
        </label>
        <Button type="submit" class="w-full" :is-loading="pending">Connect</Button>
      </form>
      <p class="mt-4 text-xs text-muted-foreground">The key stays in memory until you disconnect or close this page.</p>
    </Card>
    <p v-if="failure" class="rounded-xl border border-destructive/25 bg-card p-4 text-sm text-destructive" role="alert">
      {{ failure }}
    </p>
    <p v-if="notice" class="text-sm text-primary" role="status">{{ notice }}</p>

    <template v-if="connected">
      <div class="flex flex-wrap items-end justify-between gap-4">
        <div class="w-full max-w-sm space-y-2">
          <Field label="Container">
            <SelectPicker v-model="containerId" :options="containerOptions">
              <SelectPickerTrigger><SelectPickerValue placeholder="Choose a container" /></SelectPickerTrigger>
              <SelectPickerContent>
                <SelectPickerItem v-for="option in containerOptions" :key="option.itemKey" v-bind="option" />
              </SelectPickerContent>
            </SelectPicker>
          </Field>
        </div>
        <div class="flex gap-2">
          <Button variant="outline" :is-loading="pending" @click="refresh">Refresh</Button>
          <Button variant="ghost" @click="disconnect">Disconnect</Button>
        </div>
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
        <nav class="flex gap-1 overflow-x-auto border-b border-border pb-2" aria-label="Harvest sections">
          <Button
            v-for="tab in tabs"
            :key="tab"
            :variant="activeTab === tab ? 'outline' : 'ghost'"
            :aria-current="activeTab === tab ? 'page' : undefined"
            @click="activeTab = tab"
          >
            {{ tab }}
          </Button>
        </nav>

        <div v-if="activeTab === 'Pipelines'" class="grid gap-6 xl:grid-cols-[1fr_22rem]">
          <Card variant="glass" class="p-6">
            <h2 class="text-lg font-semibold">Pipelines</h2>
            <p class="mt-1 text-sm text-muted-foreground">A standing harvest of a source, with its own schedule.</p>
            <ul class="mt-6 divide-y divide-border" aria-label="Pipelines">
              <li v-for="pipeline in pipelines" :key="pipeline.id" class="space-y-3 py-5">
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
                      .map((schedule) => `${schedule.kind} · ${schedule.every ?? schedule.mode}`)
                      .join(' / ')
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
            <p v-if="!pipelines.length" class="mt-8 text-sm text-muted-foreground">
              No pipelines yet. Add a source, then schedule its first harvest.
            </p>
          </Card>
          <Card variant="glass" class="self-start p-6">
            <h3 class="font-semibold">Schedule a pipeline</h3>
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
                <span>Starting URL</span>
                <TextInput v-model="startUrl" placeholder="Source root or custom sitemap" />
              </label>
              <label class="block space-y-2 text-sm">
                <span>Run every (minutes)</span>
                <NumberInput v-model="everyMinutes" :min="5" :max="525600" />
              </label>
              <Button type="submit" :is-disabled="!sourceId" :is-loading="pending">Create pipeline</Button>
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
                    </SelectPickerContent>
                  </SelectPicker>
                </Field>
              </div>
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
            <template v-if="selectedPost">
              <h3 class="text-lg font-semibold">{{ selectedPost.base.title ?? 'Post details' }}</h3>
              <p class="mt-4 whitespace-pre-wrap text-sm leading-relaxed">{{ selectedPost.base.text }}</p>
              <h4 class="mt-6 text-sm font-semibold">Source links</h4>
              <ul class="mt-3 space-y-3">
                <li v-for="link in selectedPost.links" :key="link.id" class="break-all text-sm">
                  <a
                    :href="webUrl(link.url)"
                    target="_blank"
                    rel="noopener noreferrer"
                    class="text-primary underline underline-offset-4"
                  >
                    {{ link.url }}
                  </a>
                  <p class="mt-1 text-xs text-muted-foreground">{{ link.state }} · {{ date(link.capturedAtUtc) }}</p>
                </li>
              </ul>
              <h4 class="mt-6 text-sm font-semibold">Field sources</h4>
              <dl class="mt-3 space-y-2 text-xs">
                <div v-for="(linkId, field) in selectedPost.fieldSources" :key="field">
                  <dt class="font-medium">{{ field }}</dt>
                  <dd class="mt-1 break-all text-muted-foreground">
                    {{ selectedPost.links.find((link) => link.id === linkId)?.url ?? linkId }}
                  </dd>
                </div>
              </dl>
            </template>
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
                    {{ pipelines.find((item) => item.id === subscription.pipelineId)?.name ?? 'Pipeline subscription' }}
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
                      <SelectPickerTrigger><SelectPickerValue placeholder="Choose a pipeline" /></SelectPickerTrigger>
                      <SelectPickerContent>
                        <SelectPickerItem v-for="option in pipelineOptions" :key="option.itemKey" v-bind="option" />
                      </SelectPickerContent>
                    </SelectPicker>
                  </Field>
                </div>
                <Button type="submit" :is-disabled="!pipelineId || !tenantId" :is-loading="pending">Subscribe</Button>
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
          <p v-if="!hosts.length" class="mt-7 text-sm text-muted-foreground">Host lanes appear when work is queued.</p>
        </Card>
      </template>
    </template>
  </div>
</template>
