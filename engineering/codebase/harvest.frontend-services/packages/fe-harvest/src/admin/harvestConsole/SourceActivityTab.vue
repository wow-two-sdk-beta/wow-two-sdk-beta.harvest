<script setup lang="ts">
import { computed, ref } from 'vue';
import type { SourceOverviewModel } from '../../contracts';
import FilterToggleGroup from './FilterToggleGroup.vue';
import HarvestPill from './HarvestPill.vue';
import WorkerOffNotice from './WorkerOffNotice.vue';
import { isFailedRun } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';
import { sectionGlyphOf } from './PostGlyphs';
import { runFoundWords, runKindWords, runProblem, statusWords, whenOf } from './SourceWords';

/**
 * A source's runs, newest first: when, which section, what ran, how it ended and what it found. A failure's kind is a
 * small label and its message its own sentence. Chips narrow the list to the runs that need attention or one section.
 */
defineOptions({ name: 'SourceActivityTab' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const filter = ref<string | null>('all');
const expanded = ref<string>();

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
    icon: sectionGlyphOf(session.sectionParts(id).name),
    count: runs.value.filter((run) => run.pipelineId === id).length,
  })),
);
const attention = computed(() => runs.value.filter(isFailedRun));
const filters = computed(() => [
  { key: 'all', label: 'All', count: runs.value.length, icon: 'all' as const },
  { key: 'attention', label: 'Needs attention', count: attention.value.length, icon: 'attention' as const },
  ...sections.value,
]);
const shown = computed(() =>
  !filter.value || filter.value === 'all'
    ? runs.value
    : filter.value === 'attention'
      ? attention.value
      : runs.value.filter((run) => run.pipelineId === filter.value),
);
const rows = computed(() => shown.value.map((run) => ({ run, problem: runProblem(run), found: runFoundWords(run) })));
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <WorkerOffNotice />
    <FilterToggleGroup
      :model-value="filter"
      label="Filter runs"
      :items="filters"
      @update:model-value="filter = $event ?? 'all'"
    />
    <p v-if="!shown.length" class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground">
      {{ runs.length ? 'No run matches.' : 'No harvests yet. They appear here as soon as one is queued.' }}
    </p>
    <div v-else class="@container rounded-2xl border border-border bg-card">
      <table class="w-full text-sm @max-[46rem]:block @min-[46rem]:table-fixed" role="table" aria-label="Runs">
        <thead class="@max-[46rem]:sr-only" role="rowgroup">
          <tr class="border-b border-border text-left text-[13px] text-muted-foreground">
            <th class="w-[9rem] px-4 py-2.5 font-semibold">When</th>
            <th class="w-[24%] px-3 py-2.5 font-semibold">Section</th>
            <th class="w-[5.5rem] px-3 py-2.5 font-semibold">Run</th>
            <th class="w-[7rem] px-3 py-2.5 font-semibold">Result</th>
            <th class="px-3 py-2.5 pe-5 font-semibold">What it found</th>
          </tr>
        </thead>
        <tbody class="@max-[46rem]:block" role="rowgroup">
          <template v-for="{ run, problem, found } in rows" :key="run.id">
            <tr
              class="border-b border-border align-middle last:border-b-0 @min-[46rem]:h-12 @max-[46rem]:grid @max-[46rem]:grid-cols-[minmax(0,1fr)_auto] @max-[46rem]:items-center @max-[46rem]:gap-x-3 @max-[46rem]:gap-y-1 @max-[46rem]:p-3"
              role="row"
            >
              <td
                class="whitespace-nowrap @min-[46rem]:px-4 @max-[46rem]:text-xs @max-[46rem]:text-muted-foreground"
                role="cell"
              >
                {{ whenOf(run.finishedAtUtc ?? run.queuedAtUtc) }}
              </td>
              <td
                class="min-w-0 truncate font-semibold @min-[46rem]:px-3 @max-[46rem]:order-3"
                role="cell"
                :title="session.sectionLabel(run.pipelineId)"
              >
                {{ session.sectionLabel(run.pipelineId) }}
              </td>
              <td
                class="@min-[46rem]:px-3 @max-[46rem]:order-4 @max-[46rem]:text-end @max-[46rem]:text-xs @max-[46rem]:text-muted-foreground"
                role="cell"
              >
                {{ runKindWords(run.kind) }}
              </td>
              <td class="@min-[46rem]:px-3 @max-[46rem]:order-2 @max-[46rem]:justify-self-end" role="cell">
                <HarvestPill>{{ statusWords(run.status) }}</HarvestPill>
              </td>
              <td
                class="min-w-0 @min-[46rem]:px-3 @min-[46rem]:pe-5 @max-[46rem]:order-5 @max-[46rem]:col-span-full"
                role="cell"
              >
                <button
                  v-if="problem?.sentence"
                  type="button"
                  class="inline-flex max-w-full items-center gap-1.5 rounded py-1 text-start font-medium text-primary hover:underline"
                  :aria-expanded="expanded === run.id"
                  :aria-controls="`run-detail-${run.id}`"
                  @click="expanded = expanded === run.id ? undefined : run.id"
                >
                  <span class="truncate">{{ problem.label }}</span>
                  <span aria-hidden="true" :class="expanded === run.id ? 'rotate-90' : ''">›</span>
                </button>
                <span v-else class="block truncate" :title="found">{{ problem?.label ?? found }}</span>
              </td>
            </tr>
            <tr
              v-if="problem?.sentence"
              v-show="expanded === run.id"
              class="border-b border-border bg-muted/40 last:border-b-0 @max-[46rem]:block"
              role="row"
            >
              <td :id="`run-detail-${run.id}`" colspan="5" class="px-4 py-3 @max-[46rem]:block" role="cell">
                <p class="flex flex-wrap items-baseline gap-x-2 gap-y-1 text-sm" v-bind="{ 'data-run-problem': '' }">
                  <span class="font-semibold" v-bind="{ 'data-problem-label': '' }">{{ problem.label }}</span>
                  <span class="min-w-0 wrap-anywhere text-muted-foreground" v-bind="{ 'data-problem-sentence': '' }">
                    {{ problem.sentence }}
                  </span>
                </p>
              </td>
            </tr>
          </template>
        </tbody>
      </table>
    </div>
  </div>
</template>
