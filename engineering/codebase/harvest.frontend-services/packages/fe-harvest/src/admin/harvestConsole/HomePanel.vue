<script setup lang="ts">
import { computed } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Card, EmptyState, StatCard } from '@wow-two-beta/fe/presentation/display';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import WorkerOffNotice from './WorkerOffNotice.vue';
import { isActiveRun, isFailedRun } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';
import { plural, runFoundWords, StateWords, stateTone, statusTone, statusWords, whenOf } from './SourceWords';

/** The console's home: how the open workspace is doing — harvests, failures, sources needing attention, delivery. */
defineOptions({ name: 'HomePanel' });
const session = useHarvestConsole();
const { containers, runs, overviews, subscriptions, pipelines, can, pending, createPool } = session;

const activeRuns = computed(() => runs.value.filter(isActiveRun).length);
const failedRuns = computed(() => runs.value.filter(isFailedRun).length);
const attention = computed(() => overviews.value.filter((source) => source.state === 'needsAttention'));
const harvested = computed(() => overviews.value.filter((source) => source.sections.some((row) => row.harvesting)));
const activeSubscriptions = computed(() => subscriptions.value.filter((item) => item.state === 'active').length);
const latestRuns = computed(() => runs.value.slice(0, 6));
</script>

<template>
  <div class="space-y-6">
    <Card v-if="!containers.length" class="p-2">
      <EmptyState
        title="Start with the shared pool"
        description="The shared pool holds the sources and the posts your subscribers follow."
      >
        <template #actions>
          <Button v-if="can('tenants:manage') && can('containers:manage')" :is-loading="pending" @click="createPool">
            Create shared pool
          </Button>
        </template>
      </EmptyState>
    </Card>
    <template v-else>
      <section aria-label="Health" class="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Harvests running"
          :value="activeRuns"
          :helper="`Across ${plural(pipelines.length, 'section')}`"
        />
        <StatCard label="Failed harvests" :value="failedRuns" helper="Failed, partial or interrupted, recently" />
        <StatCard
          label="Sources needing attention"
          :value="attention.length"
          :helper="`${harvested.length} of ${plural(overviews.length, 'source')} harvested`"
        />
        <StatCard
          label="Delivery lag"
          value="—"
          :helper="`Not reported yet · ${plural(activeSubscriptions, 'active subscription')}`"
        />
      </section>
      <WorkerOffNotice />
      <div class="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <Card class="p-6">
          <h2 class="mb-4 text-base font-semibold">Latest harvests</h2>
          <ul v-if="latestRuns.length" class="divide-y divide-border" aria-label="Latest harvests">
            <li v-for="run in latestRuns" :key="run.id" class="flex flex-wrap items-center gap-x-3 gap-y-1 py-3">
              <HarvestPill :tone="statusTone(run.status)">{{ statusWords(run.status) }}</HarvestPill>
              <ConsoleLink
                v-if="session.pipelineOf(run.pipelineId)"
                :to="{ kind: 'source', id: session.pipelineOf(run.pipelineId)!.sourceId, tab: 'activity' }"
                class="min-w-0 truncate text-sm font-medium text-primary hover:underline"
              >
                {{ session.sourceName(session.pipelineOf(run.pipelineId)!.sourceId) }} ·
                {{ session.sectionLabel(run.pipelineId) }}
              </ConsoleLink>
              <span class="text-xs text-muted-foreground">{{ whenOf(run.finishedAtUtc ?? run.queuedAtUtc) }}</span>
              <span class="basis-full text-sm text-muted-foreground">{{ runFoundWords(run) }}</span>
            </li>
          </ul>
          <p v-else class="text-sm text-muted-foreground">No harvests yet.</p>
        </Card>
        <Card class="self-start p-6">
          <h2 class="mb-4 text-base font-semibold">Needs attention</h2>
          <ul v-if="attention.length" class="divide-y divide-border" aria-label="Sources needing attention">
            <li v-for="source in attention" :key="source.sourceId" class="flex items-center justify-between gap-3 py-3">
              <ConsoleLink
                :to="{ kind: 'source', id: source.sourceId }"
                class="truncate text-sm font-medium hover:underline"
              >
                {{ source.name }}
              </ConsoleLink>
              <HarvestPill :tone="stateTone(source.state)">{{ StateWords[source.state] }}</HarvestPill>
            </li>
          </ul>
          <p v-else class="text-sm text-muted-foreground">
            {{ overviews.length ? 'Every source is fine.' : 'Sources appear here once they are registered.' }}
          </p>
        </Card>
      </div>
    </template>
  </div>
</template>
