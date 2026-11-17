<script setup lang="ts">
import { computed, shallowRef, ref, useId, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { RunModel } from '../../contracts';
import type { ScheduleModel } from '../../contracts/PipelineModel';
import type { PipelineStageModel, PipelineStageOptions } from '../../contracts/PipelineStagesModel';
import ConsoleLink from './ConsoleLink.vue';
import { useHarvestConsole } from './HarvestConsole';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { activeRun, RunTones, WorkLabels } from './PipelineActivity';
import { postBatchLimit, scheduleWords } from './PipelineSchedule';
import PipelineStageSettings from './PipelineStageSettings.vue';
import { clockOf } from './SourceWords';

const props = withDefaults(
  defineProps<{
    stage: PipelineStageModel;
    pipelineId: string;
    showDetailLink?: boolean;
  }>(),
  { showDetailLink: true },
);
const emit = defineEmits<{ changed: [run?: RunModel] }>();
const session = useHarvestConsole();
/** Stage settings follow the server's rule: `pipelines:manage` over a container the session may change. */
const canEdit = computed(() => props.stage.available && session.canWrite('pipelines:manage'));
const canRun = computed(
  () => props.stage.available && !props.stage.readinessError && session.canWrite('pipelines:run'),
);
const submitted = shallowRef<RunModel>();
const executing = computed(() => activeRun(props.stage.runs) ?? submitted.value);
const latest = computed(() => executing.value ?? props.stage.runs[0]);
const busy = ref(false);
const message = ref('');
const batchHint = useId();
const schedule = computed(() => props.stage.schedule);
const enabled = computed(() => props.stage.options.disabled !== true);
const name = computed(() => WorkLabels[props.stage.kind]);
const savedBatch = ref(props.stage.options.batchSize);
const editedBatch = ref<string>();
const batchText = computed(() => editedBatch.value ?? String(savedBatch.value));
const batchLimit = computed(() =>
  props.stage.kind === 'discover' ? undefined : postBatchLimit(props.stage.kind, props.stage.options.model),
);
const inputMax = computed(() => Math.max(batchLimit.value ?? 0, savedBatch.value));
const batchChanged = computed(
  () =>
    /^[0-9]+$/.test(batchText.value) &&
    Number(batchText.value) !== savedBatch.value &&
    Number(batchText.value) >= 1 &&
    Number(batchText.value) <= (batchLimit.value ?? 0),
);
const stageForSettings = computed(() => ({
  ...props.stage,
  options: { ...props.stage.options, batchSize: savedBatch.value },
}));

watch(
  () => props.stage.options.batchSize,
  (size) => (savedBatch.value = size),
);

watch(
  () => props.stage.runs,
  (runs) => {
    if (submitted.value && runs.some((entry) => entry.id === submitted.value?.id)) submitted.value = undefined;
  },
);

function editBatch(event: Event) {
  const field = event.target as HTMLInputElement;
  const size = field.valueAsNumber;
  if (
    (field.value === '' && !field.validity.badInput) ||
    (/^[0-9]+$/.test(field.value) &&
      Number.isSafeInteger(size) &&
      size >= 1 &&
      (size <= (batchLimit.value ?? 0) || size === savedBatch.value))
  ) {
    editedBatch.value = field.value;
  } else {
    field.value = batchText.value;
  }
}

function restoreEmptyBatch() {
  if (editedBatch.value === '') editedBatch.value = undefined;
}

async function saveOptions(changes: Partial<PipelineStageOptions>) {
  const api = session.client.value?.pipelineStages;
  if (!api || !canEdit.value || busy.value) return false;
  const fallback = {
    kind: props.stage.kind,
    mode: 'manual' as const,
    enabled: false,
    timeZone: 'Asia/Tashkent',
  };
  const setting: Omit<ScheduleModel, 'id' | 'nextDueAtUtc'> & { id?: string; nextDueAtUtc?: string } = {
    ...(schedule.value ?? fallback),
  };
  delete setting.id;
  delete setting.nextDueAtUtc;
  const options = { ...(schedule.value?.stage ?? props.stage.options), batchSize: savedBatch.value };
  busy.value = true;
  message.value = '';
  try {
    const result = await api.save(props.pipelineId, {
      ...setting,
      stage: { ...options, ...changes },
    });
    if (result.ok) return true;
    message.value = result.failure.message || 'Stage could not be updated.';
    return false;
  } finally {
    busy.value = false;
  }
}

function batchSaved(size: number) {
  savedBatch.value = size;
  editedBatch.value = undefined;
  emit('changed');
}

async function saveBatch() {
  if (!batchChanged.value) return;
  const size = Number(batchText.value);
  if (await saveOptions({ batchSize: size })) batchSaved(size);
}

async function toggleStage() {
  if (await saveOptions({ disabled: enabled.value })) emit('changed');
}

async function run() {
  const api = session.client.value;
  if (!api || !enabled.value || busy.value || executing.value) return;
  busy.value = true;
  message.value = '';
  try {
    const result = await api.startRun(props.pipelineId, props.stage.kind);
    if (result.ok) {
      submitted.value = activeRun([result.value]);
      emit('changed', result.value);
    } else message.value = result.failure.message || 'The stage could not be queued.';
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <article class="min-w-0 rounded-2xl border border-border bg-card p-4 sm:p-5">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div class="min-w-0">
        <div class="flex flex-wrap items-center gap-2">
          <h3 class="text-base font-semibold">
            <ConsoleLink
              v-if="showDetailLink"
              :to="{ kind: 'pipeline', id: pipelineId, stage: stage.kind }"
              class="inline-flex items-center gap-1 text-primary underline-offset-4 hover:underline"
              v-bind="{ 'aria-label': `Open ${name} pipeline` }"
            >
              {{ name }}
              <PillIcon name="arrowRight" :size="14" />
            </ConsoleLink>
            <template v-else>{{ name }}</template>
          </h3>
          <HarvestPill v-if="latest" :tone="RunTones[latest.status]" class="capitalize">
            {{ latest.status }}
          </HarvestPill>
          <HarvestPill v-if="!enabled" tone="neutral">Disabled</HarvestPill>
        </div>
        <div class="mt-2 flex flex-wrap items-center gap-2">
          <form
            v-if="batchLimit !== undefined && canEdit"
            class="flex flex-wrap items-center gap-1.5"
            @submit.prevent="saveBatch"
          >
            <HarvestPill tone="outline" size="md" class="tabular-nums focus-within:border-ring">
              <PillIcon name="all" />
              <input
                type="number"
                inputmode="numeric"
                min="1"
                :max="inputMax"
                step="1"
                :value="batchText"
                :disabled="busy"
                :aria-label="`Batch size for ${name}`"
                :aria-describedby="batchHint"
                class="w-12 bg-transparent pl-1 focus-visible:outline-hidden"
                @input="editBatch"
                @blur="restoreEmptyBatch"
                @keydown.esc.stop="editedBatch = undefined"
              />
              <span>posts per run</span>
            </HarvestPill>
            <span :id="batchHint" class="sr-only">1–{{ batchLimit }} posts per run. Press Enter to save.</span>
            <Button v-if="batchChanged" type="submit" size="xs" variant="outline" :is-loading="busy">Apply</Button>
          </form>
          <HarvestPill v-else-if="batchLimit !== undefined" tone="outline" size="md" class="tabular-nums">
            <PillIcon name="all" />
            {{ savedBatch }} posts per run
          </HarvestPill>
          <PipelineStageSettings
            v-if="canEdit"
            :stage="stageForSettings"
            :pipeline-id="pipelineId"
            :is-disabled="busy"
            @saved="batchSaved"
          />
          <HarvestPill v-else tone="outline" size="md" wrap>
            <PillIcon name="clock" />
            {{ scheduleWords(schedule) }}
          </HarvestPill>
          <HarvestPill v-if="stage.pending !== undefined" tone="outline" size="md" class="tabular-nums">
            <PillIcon name="section" />
            {{ stage.pending }} pending
          </HarvestPill>
          <HarvestPill
            v-if="stage.available && enabled && schedule?.enabled && schedule.nextDueAtUtc"
            tone="outline"
            size="md"
          >
            <PillIcon name="history" />
            Next {{ clockOf(schedule.nextDueAtUtc) }}
          </HarvestPill>
        </div>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <Button
          v-if="canEdit"
          size="sm"
          variant="outline"
          :is-disabled="busy"
          :aria-label="`${enabled ? 'Disable' : 'Enable'} ${name.toLowerCase()} stage`"
          @click="toggleStage"
        >
          {{ enabled ? 'Disable' : 'Enable' }}
        </Button>
        <Button
          v-if="canRun"
          size="sm"
          :is-disabled="busy || !enabled || !!executing"
          :aria-label="`Run ${name.toLowerCase()} now`"
          @click="run"
        >
          Run now
        </Button>
      </div>
    </div>
    <p v-if="stage.readinessError" class="mt-2 text-sm text-muted-foreground">{{ stage.readinessError }}</p>
    <p v-else-if="!stage.available" class="mt-2 text-sm text-muted-foreground">
      {{
        stage.kind === 'discover' && !stage.catalog
          ? 'This section has no published discovery catalog.'
          : 'This stage is unavailable.'
      }}
    </p>
    <p v-if="message" class="mt-2 text-sm text-destructive" role="alert">{{ message }}</p>
  </article>
</template>
