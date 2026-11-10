<script setup lang="ts">
import { computed } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, BadgeVariant, Card, EmptyState, StatCard } from '@wow-two-beta/fe/presentation/display';
import ConsoleLink from './ConsoleLink.vue';
import RunList from './RunList.vue';
import WorkerOffNotice from './WorkerOffNotice.vue';
import { formatDate, isActiveRun, isFailedRun } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/** The console's home: the open container's health — runs, failures, queued work and delivery. */
defineOptions({ name: 'HomePanel' });
const { containers, runs, hosts, subscriptions, pipelines, can, pending, createPool } = useHarvestConsole();

const activeRuns = computed(() => runs.value.filter(isActiveRun).length);
const failedRuns = computed(() => runs.value.filter(isFailedRun).length);
const queued = computed(() => hosts.value.reduce((total, host) => total + host.queued, 0));
const running = computed(() => hosts.value.reduce((total, host) => total + host.running, 0));
const activeSubscriptions = computed(() => subscriptions.value.filter((item) => item.state === 'active').length);
const latestRuns = computed(() => runs.value.slice(0, 6));
/** The host lanes that wait out a pause or keep failing. */
const attention = computed(() =>
  hosts.value.filter(
    (host) => host.consecutiveFailures > 0 || (host.pausedUntilUtc && new Date(host.pausedUntilUtc) > new Date()),
  ),
);
</script>

<template>
  <div class="space-y-6">
    <Card v-if="!containers.length" class="p-2">
      <EmptyState
        title="Start with a container"
        description="A shared container holds sources and posts that your tenants can subscribe to."
      >
        <template #actions>
          <Button v-if="can('tenants:manage') && can('containers:manage')" :is-loading="pending" @click="createPool">
            Create shared container
          </Button>
        </template>
      </EmptyState>
    </Card>
    <template v-else>
      <section aria-label="Health" class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label="Active runs" :value="activeRuns" :helper="`Across ${pipelines.length} pipelines`" />
        <StatCard
          label="Failed runs"
          :value="failedRuns"
          helper="Failed, partial or interrupted, in each pipeline's latest runs"
        />
        <StatCard label="Queued work" :value="queued" :helper="`${running} running on ${hosts.length} host lanes`" />
        <StatCard
          label="Delivery lag"
          value="—"
          :helper="`Not reported yet · ${activeSubscriptions} active subscriptions`"
        />
      </section>
      <WorkerOffNotice />
      <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card class="p-6">
          <div class="mb-5 flex items-center justify-between gap-3">
            <h2 class="text-base font-semibold">Latest runs</h2>
            <ConsoleLink :to="{ kind: 'runs' }" class="text-sm text-primary hover:underline">All runs</ConsoleLink>
          </div>
          <RunList :runs="latestRuns" show-pipeline />
        </Card>
        <Card class="self-start p-6">
          <div class="mb-5 flex items-center justify-between gap-3">
            <h2 class="text-base font-semibold">Host lanes</h2>
            <ConsoleLink :to="{ kind: 'hosts' }" class="text-sm text-primary hover:underline">Hosts</ConsoleLink>
          </div>
          <ul v-if="attention.length" class="divide-y divide-border" aria-label="Host lanes needing attention">
            <li v-for="host in attention" :key="`${host.hostKey}:${host.lane}`" class="space-y-1 py-3 first:pt-0">
              <div class="flex items-center justify-between gap-3">
                <p class="truncate text-sm font-medium">{{ host.hostKey }}</p>
                <Badge :variant="BadgeVariant.Warning">{{ host.consecutiveFailures }} failures</Badge>
              </div>
              <p v-if="host.pausedUntilUtc" class="text-xs text-muted-foreground">
                Paused until {{ formatDate(host.pausedUntilUtc) }} {{ host.lastChallengeKind }}
              </p>
            </li>
          </ul>
          <p v-else class="text-sm text-muted-foreground">
            {{ hosts.length ? 'Every host lane is ready.' : 'Host lanes appear when work is queued.' }}
          </p>
        </Card>
      </div>
    </template>
  </div>
</template>
