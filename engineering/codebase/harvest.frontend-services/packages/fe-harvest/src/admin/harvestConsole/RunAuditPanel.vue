<script setup lang="ts">
import { computed, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { RunModel } from '../../contracts/EngineModels';
import type { PipelineStageKind, RunAttemptModel } from '../../contracts/PipelineStagesModel';
import { useHarvestConsole } from './HarvestConsole';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import { activeRun, runCounts, RunTones, WorkLabels } from './PipelineActivity';
import PipelineFlow from './PipelineFlow.vue';
import { webUrlOf } from './SourceWords';
import { PipelinePollMs } from './UsePipelineStages';

const props = defineProps<{
  sourceId: string;
  pipelineId?: string;
  runId?: string;
  stage?: PipelineStageKind;
  initialRun?: RunModel;
  embedded?: boolean;
  canvas?: boolean;
  operationKeys?: readonly string[];
}>();
const session = useHarvestConsole();
const client = computed(() => session.client.value?.pipelineStages);
const snapshot = shallowRef<RunModel>();
const attempts = shallowRef<readonly RunAttemptModel[]>([]);
const error = ref(false);
const offset = ref(0);
const selected = ref('');
const limit = 100;

function matches(candidate: RunModel) {
  return (
    candidate.id === props.runId &&
    (!props.stage || candidate.kind === props.stage) &&
    (props.pipelineId
      ? candidate.pipelineId === props.pipelineId
      : session.pipelineOf(candidate.pipelineId)?.sourceId === props.sourceId)
  );
}

const run = computed(() => {
  const value = snapshot.value ?? props.initialRun;
  return value && matches(value) ? value : undefined;
});
const active = computed(() => !!run.value && !!activeRun([run.value]));
const attempt = computed(
  () =>
    attempts.value.find((row) => attemptKey(row) === selected.value) ??
    attempts.value.find((row) => !row.finishedAtUtc) ??
    attempts.value[0],
);
const operations = computed(() => attempt.value?.operations ?? []);
const planned = computed(() => !active.value && !operations.value.length);
const attemptChoices = computed(() => [
  { value: '', label: 'Follow latest attempt' },
  ...attempts.value.map((row) => {
    const label = `${targetLabel(row.target)} · #${row.attempt} · ${outcome(row.outcome)}`;
    return { value: attemptKey(row), label, text: `${label} ${row.target}` };
  }),
]);

watch(
  [() => props.runId, () => props.sourceId, () => props.pipelineId, session.containerId],
  () => {
    offset.value = 0;
    selected.value = '';
    snapshot.value = undefined;
    attempts.value = [];
  },
  { flush: 'sync' },
);

watch(
  [client, () => props.runId, () => props.sourceId, () => props.pipelineId, session.loaded, offset, () => props.canvas],
  ([api, id, , pipelineId, loaded, page, canvas], _, cancel) => {
    if (!api || !id || (!pipelineId && !loaded)) return;
    const runId = id;
    attempts.value = [];
    error.value = false;
    const controller = new AbortController();
    let reading = false;
    async function load() {
      if (!api || reading) return;
      reading = true;
      try {
        const execution = await api.run(runId, controller.signal);
        if (controller.signal.aborted) return;
        if (!execution.ok || !matches(execution.value)) {
          error.value = true;
          return;
        }
        snapshot.value = execution.value;
        // Read the attempts after the status so a terminal snapshot includes its last finished step.
        const steps = await api.audit(runId, page, limit, controller.signal, canvas);
        if (controller.signal.aborted) return;
        error.value = !steps.ok;
        if (steps.ok) attempts.value = steps.value;
      } catch {
        if (!controller.signal.aborted) error.value = true;
      } finally {
        reading = false;
      }
    }
    void load();
    const timer = window.setInterval(
      () => {
        if ((active.value || (run.value && error.value)) && !document.hidden) void load();
      },
      canvas ? 1000 : PipelinePollMs,
    );
    cancel(() => {
      controller.abort();
      window.clearInterval(timer);
    });
  },
  { immediate: true },
);

function timestamp(value: string) {
  return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' });
}

function outcome(value?: string) {
  if (!value) return 'Running';
  return value.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
}

function attemptKey(row: RunAttemptModel) {
  return `${row.workItemId}:${row.attempt}`;
}

function targetLabel(target: string) {
  const href = webUrlOf(target);
  if (!href) return target;
  const url = new URL(href);
  const page = url.searchParams.get('page');
  if (page && /^\d+$/.test(page)) return `Page ${page} · ${url.hostname}`;
  return url.pathname.split('/').filter(Boolean).at(-1) ?? url.hostname;
}

function movePage(delta: number) {
  selected.value = '';
  offset.value += delta * limit;
}
</script>

<template>
  <div class="grid min-w-0 gap-5">
    <ConsoleLink
      v-if="!embedded"
      :to="{ kind: 'source', id: sourceId, tab: 'pipelines' }"
      class="w-fit text-sm text-primary hover:underline"
    >
      Back to pipelines
    </ConsoleLink>
    <p v-if="error || !client" class="text-sm text-muted-foreground" role="alert">
      {{ run ? 'Some run details could not be loaded.' : 'The run audit could not be loaded.' }}
    </p>
    <p v-else-if="runId && !run" class="text-sm text-muted-foreground" role="status">Loading run…</p>
    <template v-if="run">
      <header class="rounded-xl border border-border bg-background/45 p-4">
        <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
          <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            <component :is="embedded ? 'h3' : 'h1'" class="text-lg font-semibold">
              {{ WorkLabels[run.kind] }} run
            </component>
            <HarvestPill :tone="RunTones[run.status]" class="capitalize" v-bind="{ 'aria-live': 'polite' }">
              {{ outcome(run.status) }}
            </HarvestPill>
          </div>
          <p class="text-sm tabular-nums" aria-live="polite">{{ runCounts(run) }} · {{ run.requests }} requests</p>
        </div>
        <p class="mt-1 text-xs text-muted-foreground">
          Queued {{ timestamp(run.queuedAtUtc) }}
          <template v-if="run.finishedAtUtc">· Finished {{ timestamp(run.finishedAtUtc) }}</template>
          <template v-if="run.completionReason && !run.message">· {{ outcome(run.completionReason) }}</template>
        </p>
        <p v-if="run.message" class="mt-2 text-sm wrap-anywhere text-muted-foreground">
          <template v-if="run.completionReason">{{ outcome(run.completionReason) }} ·</template>
          {{ run.message }}
        </p>
      </header>
      <section
        v-if="client && !canvas"
        class="grid min-w-0 gap-3"
        aria-label="Step attempts"
        :class="{ 'rounded-2xl border border-border bg-card p-5': !embedded }"
      >
        <component :is="embedded ? 'h3' : 'h2'" class="text-base font-semibold">Step attempts</component>
        <p v-if="!attempts.length && !error" class="text-sm text-muted-foreground">
          {{ active ? 'Waiting for the worker.' : 'No recorded attempts on this page.' }}
        </p>
        <div v-else-if="attempts.length" class="overflow-x-auto">
          <table class="w-full text-left text-sm">
            <thead class="text-muted-foreground">
              <tr class="border-b border-border">
                <th scope="col" class="py-2 pr-4">Step</th>
                <th scope="col" class="py-2 pr-4">Target</th>
                <th scope="col" class="py-2 pr-4">Started</th>
                <th scope="col" class="py-2">Outcome</th>
                <th scope="col" class="py-2"><span class="sr-only">Operations</span></th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="step in attempts"
                :key="attemptKey(step)"
                class="border-b border-border align-top"
                :class="{ 'bg-primary/8': step === attempt }"
              >
                <td class="whitespace-nowrap py-3 pr-4">{{ step.step }} · #{{ step.attempt }}</td>
                <td class="max-w-96 break-all py-3 pr-4 text-muted-foreground">{{ step.target }}</td>
                <td class="whitespace-nowrap py-3 pr-4">{{ timestamp(step.startedAtUtc) }}</td>
                <td class="min-w-40 py-3">
                  <div class="capitalize">
                    {{ outcome(step.outcome) }}
                    <template v-if="step.httpStatus">· HTTP {{ step.httpStatus }}</template>
                  </div>
                  <div v-if="step.discovered" class="text-muted-foreground">{{ step.discovered }} found</div>
                  <div v-if="step.message" class="text-muted-foreground">{{ step.message }}</div>
                  <div v-if="step.retryAtUtc" class="text-muted-foreground">
                    Retry after {{ timestamp(step.retryAtUtc) }}
                  </div>
                </td>
                <td class="py-3 pl-3">
                  <Button
                    v-if="step.operations?.length"
                    size="sm"
                    variant="outline"
                    v-bind="{ 'aria-pressed': step === attempt }"
                    @click="selected = attemptKey(step)"
                  >
                    Inspect steps
                  </Button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div v-if="offset || attempts.length === limit" class="flex gap-2">
          <Button size="sm" variant="outline" :is-disabled="offset === 0" @click="movePage(-1)">Previous</Button>
          <Button size="sm" variant="outline" :is-disabled="attempts.length < limit" @click="movePage(1)">Next</Button>
        </div>
      </section>
    </template>
    <section
      v-if="canvas || operations.length"
      class="grid min-w-0 gap-3 rounded-2xl border border-border bg-card p-4 sm:p-5"
      :aria-label="canvas ? 'Pipeline canvas' : 'Attempt operations'"
    >
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 class="font-semibold">
          {{ canvas ? (planned ? 'Planned steps' : 'Pipeline steps') : 'Attempt operations' }}
        </h2>
        <div v-if="canvas && attempts.length > 1" class="w-full min-w-0 sm:w-96">
          <ChoicePicker
            v-model="selected"
            :choices="attemptChoices"
            label="Attempt target"
            has-trigger-width
            is-searchable
          />
        </div>
      </div>
      <div v-if="attempt" class="grid min-w-0 gap-0.5 text-sm break-words text-muted-foreground">
        <span>{{ attempt.target }} · attempt {{ attempt.attempt }} · {{ outcome(attempt.outcome) }}</span>
        <span v-if="attempt.message">{{ attempt.message }}</span>
        <span v-if="attempt.retryAtUtc">Retry after {{ timestamp(attempt.retryAtUtc) }}</span>
      </div>
      <PipelineFlow
        v-if="operations.length || operationKeys?.length"
        :key="attempt ? attemptKey(attempt) : 'plan'"
        :operation-keys="operationKeys ?? []"
        :operations="operations"
        :show-pending="planned || !attempt || !attempt.finishedAtUtc"
        :pending-state="planned ? 'planned' : 'pending'"
      />
      <p v-else-if="attempt" class="text-sm text-muted-foreground">This attempt has an outcome record.</p>
      <p v-else class="text-sm text-muted-foreground">Waiting for the pipeline plan.</p>
      <div v-if="canvas && (offset || attempts.length === limit)" class="flex gap-2">
        <Button size="sm" variant="outline" :is-disabled="offset === 0" @click="movePage(-1)">Newer attempts</Button>
        <Button size="sm" variant="outline" :is-disabled="attempts.length < limit" @click="movePage(1)">
          Older attempts
        </Button>
      </div>
    </section>
  </div>
</template>
