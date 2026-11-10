<script setup lang="ts">
import { computed, ref } from 'vue';
import type { RunModel, SourceOverviewModel } from '../../contracts';
import FilterChip from './FilterChip.vue';
import HarvestPill from './HarvestPill.vue';
import WorkerOffNotice from './WorkerOffNotice.vue';
import { isFailedRun } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';
import { runFoundWords, runKindWords, runProblem, statusTone, statusWords, whenOf } from './SourceWords';

/**
 * A source's runs, newest first: when, which section, what ran, how it ended and what it found. A failure's kind is a
 * small label and its message its own sentence. Chips narrow the list to the runs that need attention or one section.
 */
defineOptions({ name: 'SourceActivityTab' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const filter = ref<string>('all');

const pipelineIds = computed(
  () =>
    new Set([
      ...props.source.sections.map((section) => section.pipelineId),
      ...session.pipelines.value.filter((item) => item.sourceId === props.source.sourceId).map((item) => item.id),
    ]),
);
const runs = computed(() => session.runs.value.filter((run) => pipelineIds.value.has(run.pipelineId)));
const sections = computed(() =>
  [...new Set(runs.value.map((run) => run.pipelineId))].map((id) => ({
    key: id,
    label: session.sectionLabel(id),
    count: runs.value.filter((run) => run.pipelineId === id).length,
  })),
);
const attention = computed(() => runs.value.filter(isFailedRun));
const shown = computed(() =>
  filter.value === 'all'
    ? runs.value
    : filter.value === 'attention'
      ? attention.value
      : runs.value.filter((run) => run.pipelineId === filter.value),
);

/** The page limit of the section a run belongs to, for `stopped at the 5-page limit`. */
function maxPagesOf(run: RunModel) {
  const limit = session.pipelineOf(run.pipelineId)?.limits['maxPages'];
  const value = typeof limit === 'number' ? limit : Number(limit);
  return Number.isFinite(value) && value > 0
    ? value
    : props.source.sections.find((section) => section.pipelineId === run.pipelineId)?.maxPages;
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <WorkerOffNotice />
    <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter runs">
      <FilterChip label="All" :count="runs.length" :is-pressed="filter === 'all'" @press="filter = 'all'" />
      <FilterChip
        label="Needs attention"
        :count="attention.length"
        :is-pressed="filter === 'attention'"
        @press="filter = 'attention'"
      />
      <FilterChip
        v-for="chip in sections"
        :key="chip.key"
        :label="chip.label"
        :count="chip.count"
        :is-pressed="filter === chip.key"
        @press="filter = chip.key"
      />
    </div>
    <p v-if="!shown.length" class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground">
      {{ runs.length ? 'No run matches.' : 'No harvests yet. They appear here as soon as one is queued.' }}
    </p>
    <div v-else class="overflow-x-auto rounded-2xl border border-border bg-card">
      <table class="w-full min-w-[44rem] text-[15px]" aria-label="Runs">
        <thead>
          <tr class="border-b border-border text-left text-[13px] text-muted-foreground">
            <th class="px-5 py-2.5 font-semibold">When</th>
            <th class="px-3 py-2.5 font-semibold">Section</th>
            <th class="px-3 py-2.5 font-semibold">Run</th>
            <th class="px-3 py-2.5 font-semibold">Result</th>
            <th class="px-3 py-2.5 pe-5 font-semibold">What it found</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="run in shown" :key="run.id" class="border-b border-border align-top last:border-b-0">
            <td class="px-5 py-3 whitespace-nowrap">{{ whenOf(run.finishedAtUtc ?? run.queuedAtUtc) }}</td>
            <td class="px-3 py-3 font-semibold">{{ session.sectionLabel(run.pipelineId) }}</td>
            <td class="px-3 py-3">{{ runKindWords(run.kind) }}</td>
            <td class="px-3 py-3">
              <HarvestPill :tone="statusTone(run.status)">{{ statusWords(run.status) }}</HarvestPill>
            </td>
            <td class="px-3 py-3 pe-5">
              <p>{{ runFoundWords(run, maxPagesOf(run)) }}</p>
              <p
                v-if="runProblem(run)"
                class="mt-1 flex flex-wrap items-baseline gap-2 text-sm"
                v-bind="{ 'data-run-problem': '' }"
              >
                <span
                  class="rounded-md bg-destructive-soft px-1.5 py-0.5 text-xs font-semibold text-destructive-soft-foreground"
                  v-bind="{ 'data-problem-label': '' }"
                >
                  {{ runProblem(run)!.label }}
                </span>
                <span
                  v-if="runProblem(run)!.sentence"
                  class="text-muted-foreground"
                  v-bind="{ 'data-problem-sentence': '' }"
                >
                  {{ runProblem(run)!.sentence }}
                </span>
              </p>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>
