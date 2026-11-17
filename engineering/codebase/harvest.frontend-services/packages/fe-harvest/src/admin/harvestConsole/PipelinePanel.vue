<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { RunModel, WorkKind } from '../../contracts';
import type { PipelineStageKind } from '../../contracts/PipelineStagesModel';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import { useHarvestConsole, type PipelineTab } from './HarvestConsole';
import HarvestPill from './HarvestPill.vue';
import { activeRun, runCounts, RunTones, WorkLabels } from './PipelineActivity';
import PipelineDiscoveries from './PipelineDiscoveries.vue';
import PipelineStageCard from './PipelineStageCard.vue';
import RunAuditPanel from './RunAuditPanel.vue';
import { clockOf, statusWords } from './SourceWords';
import { usePipelineStages } from './UsePipelineStages';

const props = defineProps<{ pipelineId: string; stage?: PipelineStageKind; runId?: string; tab?: PipelineTab }>();
const session = useHarvestConsole();
const canRead = computed(() => session.can('pipelines:read'));
const pipeline = computed(() => (canRead.value ? session.pipelineOf(props.pipelineId) : undefined));
const { stages, loading, error, refresh } = usePipelineStages(() => pipeline.value?.id ?? '');
const kind = ref<WorkKind | 'all'>('all');
const submitted = shallowRef<RunModel>();
const changingPipeline = ref(false);
const tab = computed(() => (props.runId ? 'logs' : (props.tab ?? 'overview')));
const place = computed(() => ({
  kind: 'pipeline' as const,
  id: props.pipelineId,
  ...(props.stage ? { stage: props.stage } : {}),
}));
const tabs = [
  { id: 'overview', label: 'Overview' },
  { id: 'logs', label: 'Logs' },
  { id: 'discoveries', label: 'Discoveries' },
] as const;
const canPause = computed(
  () =>
    session.canOperate.value &&
    session.canWrite('pipelines:manage') &&
    (pipeline.value?.state === 'active' || pipeline.value?.pauseReason === 'operator'),
);

const knownRuns = computed(() => {
  const byId = new Map(session.runsOf(props.pipelineId).map((run) => [run.id, run]));
  for (const stage of stages.value) {
    for (const run of stage.runs) byId.set(run.id, run);
  }
  return byId;
});

watch(knownRuns, (rows) => {
  if (submitted.value && rows.has(submitted.value.id)) submitted.value = undefined;
});

const recent = computed(() => {
  const rows = [...knownRuns.value.values()];
  if (submitted.value && !knownRuns.value.has(submitted.value.id)) rows.push(submitted.value);
  return rows.sort((a, b) => b.queuedAtUtc.localeCompare(a.queuedAtUtc) || b.id.localeCompare(a.id));
});
const kinds = computed(() => [
  ...new Set([...stages.value.map((stage) => stage.kind), ...recent.value.map((run) => run.kind)]),
]);
const historyChoices = computed(() => [
  { value: 'all' as const, label: 'All stages' },
  ...kinds.value.map((value) => ({ value, label: WorkLabels[value] })),
]);
const filter = computed(() => props.stage ?? (tab.value === 'logs' ? kind.value : 'all'));
const history = computed(() => recent.value.filter((run) => filter.value === 'all' || run.kind === filter.value));
const current = computed(() => activeRun(history.value));
const selectedId = computed(() => props.runId ?? current.value?.id ?? history.value[0]?.id);
const selectedRun = computed(() => recent.value.find((run) => run.id === selectedId.value));
const selectedStage = computed(() => stages.value.find((stage) => stage.kind === props.stage));

function stageChanged(run?: RunModel) {
  if (run) submitted.value = run;
  refresh();
}

async function togglePipeline() {
  const row = pipeline.value;
  if (!row || !canPause.value || changingPipeline.value) return;
  changingPipeline.value = true;
  try {
    await session.setPipelinePaused(row.id, row.state === 'active');
    refresh();
  } finally {
    changingPipeline.value = false;
  }
}
</script>

<template>
  <div v-if="pipeline" class="grid min-w-0 gap-5">
    <header
      class="flex flex-wrap items-center justify-between gap-4 rounded-2xl border border-border bg-card p-5 sm:p-6"
    >
      <div class="min-w-0">
        <ConsoleLink
          :to="
            stage ? { kind: 'pipeline', id: pipeline.id } : { kind: 'source', id: pipeline.sourceId, tab: 'pipelines' }
          "
          class="text-sm font-medium text-primary hover:underline"
        >
          {{ stage ? session.pipelineLabel(pipeline) : session.sourceName(pipeline.sourceId) }}
        </ConsoleLink>
        <h1 class="mt-0.5 break-words text-2xl font-semibold tracking-tight">
          {{ stage ? `${WorkLabels[stage]} pipeline` : session.pipelineLabel(pipeline) }}
        </h1>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <HarvestPill v-if="pipeline.state !== 'active'" tone="warning" class="capitalize">
          {{ pipeline.state === 'pendingApproval' ? 'Pending approval' : pipeline.state }}
        </HarvestPill>
        <Button
          v-if="!stage && canPause"
          size="sm"
          variant="outline"
          :is-disabled="changingPipeline || session.pending.value"
          @click="togglePipeline"
        >
          {{ pipeline.state === 'active' ? 'Pause pipeline' : 'Resume pipeline' }}
        </Button>
        <a
          v-if="selectedStage?.catalog?.url.startsWith('https://')"
          :href="selectedStage.catalog.url"
          target="_blank"
          rel="noopener noreferrer"
          class="text-sm font-medium text-primary hover:underline"
        >
          {{ selectedStage.catalog.label }} ↗
        </a>
      </div>
    </header>
    <nav aria-label="Pipeline views" class="flex gap-1 border-b border-border">
      <ConsoleLink
        v-for="view in tabs"
        :key="view.id"
        :to="{ ...place, tab: view.id }"
        v-bind="{ 'aria-current': tab === view.id ? 'page' : undefined }"
        class="border-b-2 px-3 py-2.5 text-sm font-medium transition sm:px-4"
        :class="
          tab === view.id
            ? 'border-primary text-foreground'
            : 'border-transparent text-muted-foreground hover:text-foreground'
        "
      >
        {{ view.label }}
      </ConsoleLink>
    </nav>
    <template v-if="tab === 'overview'">
      <p v-if="error" role="alert" class="text-sm text-muted-foreground">Pipeline status could not be refreshed.</p>
      <p v-else-if="loading" role="status" class="text-sm text-muted-foreground">Loading pipelines…</p>
      <template v-if="selectedStage">
        <PipelineStageCard
          :stage="selectedStage"
          :pipeline-id="pipeline.id"
          :show-detail-link="false"
          @changed="stageChanged"
        />
        <RunAuditPanel
          :key="selectedId ?? selectedStage.kind"
          :source-id="pipeline.sourceId"
          :pipeline-id="pipeline.id"
          :stage="selectedStage.kind"
          :operation-keys="selectedStage.operationKeys ?? []"
          v-bind="{ ...(selectedId ? { runId: selectedId } : {}), ...(selectedRun ? { initialRun: selectedRun } : {}) }"
          canvas
          embedded
        />
      </template>
      <p v-else-if="stage && !loading && !error" class="text-sm text-muted-foreground">This pipeline is unavailable.</p>
      <section v-else-if="!stage" class="grid gap-4" aria-label="Section pipelines">
        <PipelineStageCard
          v-for="entry in stages"
          :key="entry.kind"
          :stage="entry"
          :pipeline-id="pipeline.id"
          @changed="stageChanged"
        />
      </section>
    </template>
    <div
      v-else-if="tab === 'logs'"
      class="grid min-w-0 items-start gap-5 xl:grid-cols-[minmax(0,1fr)_minmax(19rem,25rem)]"
    >
      <section class="grid min-w-0 gap-4 rounded-2xl border border-border bg-card p-4 sm:p-5" aria-label="Run monitor">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="font-semibold">{{ runId ? 'Selected run' : current ? 'Current run' : 'Latest run' }}</h2>
          <ConsoleLink
            v-if="runId"
            :to="{ ...place, tab: 'logs' }"
            class="text-sm font-medium text-primary hover:underline"
          >
            Follow live
          </ConsoleLink>
          <HarvestPill v-else tone="outline">Following live</HarvestPill>
        </div>
        <RunAuditPanel
          v-if="selectedId"
          :key="selectedId"
          :run-id="selectedId"
          :source-id="pipeline.sourceId"
          :pipeline-id="pipeline.id"
          v-bind="{ ...(selectedRun ? { initialRun: selectedRun } : {}), ...(stage ? { stage } : {}) }"
          embedded
        />
        <p v-else class="text-sm text-muted-foreground">
          {{ filter === 'all' ? 'No runs yet.' : 'No runs for this pipeline yet.' }}
        </p>
      </section>
      <section class="grid min-w-0 gap-3 rounded-2xl border border-border bg-card p-4 sm:p-5" aria-label="Run history">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <h2 class="font-semibold">Run history</h2>
          <div v-if="!stage && kinds.length" class="w-36">
            <ChoicePicker v-model="kind" :choices="historyChoices" label="Filter history by stage" />
          </div>
        </div>
        <p v-if="!history.length" class="text-sm text-muted-foreground">No matching runs.</p>
        <ul v-else class="max-h-96 divide-y divide-border overflow-y-auto">
          <li v-for="run in history" :key="run.id">
            <ConsoleLink
              :to="{ ...place, run: run.id }"
              v-bind="{ 'aria-current': run.id === selectedId ? 'true' : undefined }"
              class="block rounded-lg px-3 py-2.5 hover:bg-muted/50 focus-visible:outline-2 focus-visible:outline-primary"
              :class="{ 'bg-primary/10': run.id === selectedId }"
            >
              <div class="flex flex-wrap items-center justify-between gap-1.5">
                <span class="text-sm font-medium">{{ WorkLabels[run.kind] }}</span>
                <HarvestPill :tone="RunTones[run.status]" class="capitalize">
                  {{ statusWords(run.status) }}
                </HarvestPill>
              </div>
              <div class="mt-1 break-words text-xs text-muted-foreground">
                {{ clockOf(run.queuedAtUtc) }} · {{ runCounts(run) }}
              </div>
            </ConsoleLink>
          </li>
        </ul>
      </section>
    </div>
    <PipelineDiscoveries v-else :pipeline-id="pipeline.id" :source-id="pipeline.sourceId" />
  </div>
  <section v-else class="grid gap-3" aria-label="Pipeline">
    <h1 class="text-2xl font-semibold">Pipeline</h1>
    <p v-if="!canRead" class="text-sm text-muted-foreground">This session cannot read pipelines.</p>
    <p v-else-if="!session.loaded.value" class="text-sm text-muted-foreground" role="status">Loading pipeline…</p>
    <template v-else>
      <p class="text-sm text-muted-foreground">This pipeline was not found in the open workspace.</p>
      <ConsoleLink :to="{ kind: 'pipelines' }" class="w-fit text-sm text-primary hover:underline">
        Back to pipelines
      </ConsoleLink>
    </template>
  </section>
</template>
