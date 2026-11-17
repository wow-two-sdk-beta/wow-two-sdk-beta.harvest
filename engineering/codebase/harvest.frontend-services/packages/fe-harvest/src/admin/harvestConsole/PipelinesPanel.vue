<script setup lang="ts">
import { computed, ref } from 'vue';
import type { RunModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import { useHarvestConsole } from './HarvestConsole';
import HarvestPill from './HarvestPill.vue';
import { activeRun, needsAttention, runCounts, RunTones, WorkLabels } from './PipelineActivity';
import { nextAutomaticSchedule } from './PipelineSchedule';
import { clockOf, plural, statusWords } from './SourceWords';

const session = useHarvestConsole();
const search = ref('');
const filter = ref<'all' | 'active' | 'attention'>('all');
const canRead = computed(() => session.can('pipelines:read'));
const workKinds = ['discover', 'harvest', 'process'] as const;

const pipelines = computed(() => {
  const byPipeline = new Map<string, RunModel[]>();
  for (const run of session.runs.value) {
    const rows = byPipeline.get(run.pipelineId);
    if (rows) rows.push(run);
    else byPipeline.set(run.pipelineId, [run]);
  }
  return session.pipelines.value
    .map((pipeline) => {
      const runs = byPipeline.get(pipeline.id) ?? [];
      const active = activeRun(runs);
      const last = runs[0];
      return {
        pipeline,
        source: session.sourceName(pipeline.sourceId),
        label: session.pipelineLabel(pipeline),
        active,
        run: active ?? last,
        attention: needsAttention(last),
        next: pipeline.state === 'active' ? nextAutomaticSchedule(pipeline.schedules) : undefined,
      };
    })
    .sort(
      (a, b) =>
        Number(!!b.active) - Number(!!a.active) ||
        Number(b.attention) - Number(a.attention) ||
        a.source.localeCompare(b.source) ||
        a.label.localeCompare(b.label),
    );
});

const filters = computed(
  () =>
    [
      { key: 'all', label: 'All', count: pipelines.value.length },
      { key: 'active', label: 'In flight', count: pipelines.value.filter((row) => row.active).length },
      { key: 'attention', label: 'Needs attention', count: pipelines.value.filter((row) => row.attention).length },
    ] as const,
);

const visible = computed(() => {
  const query = search.value.trim().toLocaleLowerCase();
  return pipelines.value.filter(
    (row) =>
      (filter.value !== 'active' || row.active) &&
      (filter.value !== 'attention' || row.attention) &&
      `${row.source} ${row.label} ${row.pipeline.name ?? ''}`.toLocaleLowerCase().includes(query),
  );
});
</script>

<template>
  <section class="grid min-w-0 gap-4" aria-label="Pipelines">
    <p v-if="!canRead" class="text-sm text-muted-foreground">This session cannot read pipelines.</p>
    <p v-else-if="!session.loaded.value" class="text-sm text-muted-foreground" role="status">Loading pipelines…</p>
    <p v-else-if="!pipelines.length" class="text-sm text-muted-foreground">
      This workspace has no registered pipelines.
    </p>
    <template v-else>
      <div class="flex flex-wrap items-center gap-2">
        <label class="min-w-48 flex-1">
          <span class="sr-only">Search pipelines</span>
          <input
            v-model="search"
            class="w-full rounded-lg border border-border-strong bg-background px-4 py-2.5 text-sm"
            type="search"
            placeholder="Search sources and sections"
          />
        </label>
        <button
          v-for="item in filters"
          :key="item.key"
          type="button"
          :aria-pressed="filter === item.key"
          class="rounded-full border border-border px-3.5 py-2 text-sm font-medium hover:bg-muted"
          :class="{ 'border-primary/30 bg-primary/15 text-primary': filter === item.key }"
          @click="filter = item.key"
        >
          {{ item.label }}
          <span class="ml-1 tabular-nums">{{ item.count }}</span>
        </button>
      </div>
      <p v-if="!visible.length" class="text-sm text-muted-foreground" role="status">
        No pipelines match these filters.
      </p>
      <div v-else class="overflow-hidden rounded-2xl border border-border bg-card">
        <table class="w-full table-fixed text-left text-sm">
          <thead class="border-b border-border bg-muted/40 text-muted-foreground">
            <tr>
              <th scope="col" class="w-[46%] px-4 py-3 font-medium sm:w-[38%] sm:px-5">Section and pipelines</th>
              <th scope="col" class="px-4 py-3 font-medium sm:px-5">Activity</th>
              <th scope="col" class="hidden w-[22%] px-5 py-3 font-medium md:table-cell">Next run</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-border">
            <tr v-for="row in visible" :key="row.pipeline.id" class="align-middle hover:bg-muted/30">
              <td class="px-4 py-3 sm:px-5">
                <ConsoleLink
                  :to="{ kind: 'pipeline', id: row.pipeline.id }"
                  class="break-words text-base font-semibold text-foreground hover:text-primary hover:underline"
                >
                  {{ row.label }}
                </ConsoleLink>
                <div class="mt-0.5 break-words text-muted-foreground">{{ row.source }}</div>
                <div class="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-xs font-medium">
                  <ConsoleLink
                    v-for="stage in workKinds"
                    :key="stage"
                    :to="{ kind: 'pipeline', id: row.pipeline.id, stage }"
                    v-bind="{ 'aria-label': `Open ${WorkLabels[stage]} pipeline for ${row.label}` }"
                    class="text-primary hover:underline"
                  >
                    {{ WorkLabels[stage] }}
                  </ConsoleLink>
                </div>
              </td>
              <td class="px-4 py-3 sm:px-5">
                <div v-if="row.run" class="flex flex-wrap items-center gap-x-2 gap-y-1">
                  <span class="font-medium">{{ WorkLabels[row.run.kind] }}</span>
                  <HarvestPill :tone="RunTones[row.run.status]" class="capitalize">
                    {{ statusWords(row.run.status) }}
                  </HarvestPill>
                </div>
                <p v-if="row.run" class="mt-0.5 break-words text-xs text-muted-foreground">
                  {{ runCounts(row.run) }} · {{ clockOf(row.run.queuedAtUtc) }}
                </p>
                <span v-else class="text-muted-foreground">No runs yet</span>
              </td>
              <td class="hidden px-5 py-3 md:table-cell">
                <div v-if="row.next" class="font-medium">
                  {{ WorkLabels[row.next.kind] }} · {{ clockOf(row.next.nextDueAtUtc) }}
                </div>
                <div v-else-if="row.pipeline.state !== 'active'" class="capitalize text-muted-foreground">
                  {{ row.pipeline.state === 'pendingApproval' ? 'Pending approval' : row.pipeline.state }}
                </div>
                <div v-else class="text-muted-foreground">—</div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="visible.length && visible.length !== pipelines.length" class="text-xs text-muted-foreground">
        {{ plural(visible.length, 'matching pipeline') }}
      </p>
    </template>
  </section>
</template>
