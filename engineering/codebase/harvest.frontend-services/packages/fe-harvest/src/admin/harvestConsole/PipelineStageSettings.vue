<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import {
  Modal,
  ModalBody,
  ModalClose,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
  ModalTrigger,
} from '@wow-two-beta/fe/presentation/overlays';
import type { ScheduleModel } from '../../contracts';
import type { PipelineStageModel, PipelineStageOptions } from '../../contracts/PipelineStagesModel';
import ChoicePicker from './ChoicePicker.vue';
import PillIcon from './PillIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import { WorkLabels } from './PipelineActivity';
import {
  calendarCron,
  defaultCalendar,
  intervalMinutes,
  MaxGapMs,
  MaxParallel,
  minimumInterval,
  postBatchLimit,
  readCalendar,
  scheduleWords,
  walkWords,
  Weekdays,
} from './PipelineSchedule';

const props = defineProps<{ pipelineId: string; stage: PipelineStageModel; isDisabled?: boolean }>();
const emit = defineEmits<{ saved: [batchSize: number] }>();
const session = useHarvestConsole();
const formId = useId();
const open = ref(false);
const busy = ref(false);
const error = ref('');
const inputClass = 'w-full min-w-0 rounded-md border border-border bg-background px-3 py-2 text-sm';
const name = computed(() => WorkLabels[props.stage.kind]);
/** A harvest stage runs as often as every 15 seconds, so its interval is set in seconds; every other kind sets minutes. */
const inSeconds = computed(() => props.stage.kind === 'harvest');
const everyMinimum = computed(() => minimumInterval(props.stage.kind) / (inSeconds.value ? 1 : 60));
/** The gap and the parallel fetches pace a host every tenant shares, so only an operator edits them; a tenant's save keeps them. */
const canSetPacing = computed(() => session.canOperate.value);
const Walks = (['resume', 'full', 'new'] as const).map((value) => ({ value, label: walkWords(value) }));
const Repeats = [
  { value: 'daily', label: 'Daily' },
  { value: 'weekly', label: 'Weekly' },
  { value: 'monthly', label: 'Monthly' },
] as const;
const MonthDays = [
  ...Array.from({ length: 31 }, (_, index) => ({ value: String(index + 1), label: String(index + 1) })),
  { value: 'L', label: 'Last day' },
];
const SavedReading = 'saved-reading@1';
const SharedRentalTerms = 'rental-terms@2';
const MaxPromptVersion = 2_147_483_647;
const DefaultTimeZone = 'Asia/Tashkent';
const BrowserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
const TimeZones = Intl.supportedValuesOf?.('timeZone') ?? [];

const timeZoneChoices = computed(() =>
  [...new Set([DefaultTimeZone, 'UTC', BrowserTimeZone, props.stage.schedule?.timeZone, ...TimeZones])]
    .filter((zone): zone is string => !!zone)
    .map((zone) => ({ value: zone, label: zone })),
);
const scheduleChoices = computed(() => {
  const choices: { value: ScheduleModel['mode'] | 'saved'; label: string }[] = [
    { value: 'manual', label: 'On demand' },
    { value: 'interval', label: 'Fixed interval' },
    { value: 'cron', label: 'Calendar' },
  ];
  const schedule = props.stage.schedule;
  if (schedule?.mode === 'adaptive') choices.push({ value: 'adaptive', label: 'Adaptive interval' });
  if (schedule?.mode === 'cron' && !readCalendar(schedule.cron)) {
    choices.push({ value: 'saved', label: 'Saved calendar' });
  }
  return choices;
});

function fieldsOf(stage: PipelineStageModel) {
  const schedule = stage.schedule;
  const calendar = readCalendar(schedule?.cron);
  return {
    mode: schedule?.mode === 'cron' && !calendar ? ('saved' as const) : (schedule?.mode ?? 'interval'),
    enabled: schedule?.enabled ?? false,
    every: intervalCount(intervalMinutes(schedule?.every) ?? 60, stage.kind),
    maxEvery: intervalCount(intervalMinutes(schedule?.maxEvery) ?? 120, stage.kind),
    calendar: calendar ?? defaultCalendar(),
    startsOn: stage.options.startsOn ?? '',
    timeZone: schedule?.timeZone ?? DefaultTimeZone,
    titleContains: stage.options.titleContains ?? '',
    publishedSince: stage.options.publishedSinceUtc?.slice(0, 10) ?? '',
    processor: stage.options.processor ?? (stage.processorKeys?.length ? SavedReading : ''),
    model: stage.options.model ?? '',
    promptVersion: stage.options.promptVersion ?? '',
    walk: stage.options.walk ?? ('resume' as const),
    gapMs: stage.options.gapMs ?? ('' as number | ''),
    parallel: stage.options.parallel ?? 1,
  };
}

/** A count of the kind's interval unit: seconds for harvest, minutes for every other kind. */
function intervalCount(minutes: number, kind: PipelineStageModel['kind']) {
  return kind === 'harvest' ? Math.round(minutes * 60) : Math.ceil(minutes);
}
const draft = ref(fieldsOf(props.stage));
const processorChoices = computed(() =>
  (props.stage.processorKeys ?? []).map((key) => ({
    value: key,
    label: props.stage.processorLabels?.[key] ?? key,
  })),
);
const showsModel = computed(() => props.stage.kind === 'process' && draft.value.processor !== SavedReading);
const usesSharedModel = computed(() => props.stage.kind === 'process' && draft.value.processor === SharedRentalTerms);
const selectedModel = computed(() => (showsModel.value ? draft.value.model.trim() : ''));
const batchSize = computed(() => {
  const current = props.stage.options.batchSize;
  if (props.stage.kind !== 'process' || selectedModel.value === (props.stage.options.model ?? '').trim())
    return current;
  return Math.min(current, postBatchLimit('process', selectedModel.value));
});
const hasCalendar = computed(() => draft.value.mode === 'cron' || draft.value.mode === 'saved');
const needsTimeZone = computed(
  () => hasCalendar.value || (draft.value.mode !== 'manual' && !!props.stage.schedule?.window),
);
watch(open, (visible) => {
  if (visible) {
    draft.value = fieldsOf(props.stage);
    error.value = '';
  }
});

function changeOpen(value: boolean) {
  if (!busy.value) open.value = value;
}

function intervalValue(count: number, previous?: string) {
  const kind = props.stage.kind;
  return previous && intervalCount(intervalMinutes(previous) ?? 0, kind) === count
    ? previous
    : `PT${count}${inSeconds.value ? 'S' : 'M'}`;
}

/** What a save sends for the stage's requests: an operator's gap and parallel fetches, else the stored ones, so a save never drops them. */
function pacingOf(fields: ReturnType<typeof fieldsOf>): Pick<PipelineStageOptions, 'gapMs' | 'parallel'> | string {
  const stored = props.stage.options;
  if (!canSetPacing.value) {
    return {
      ...(stored.gapMs !== undefined ? { gapMs: stored.gapMs } : {}),
      ...(stored.parallel !== undefined ? { parallel: stored.parallel } : {}),
    };
  }
  const gap = fields.gapMs === '' ? undefined : Number(fields.gapMs);
  if (gap !== undefined && (!Number.isInteger(gap) || gap < 0 || gap > MaxGapMs))
    return `Enter a whole gap from 0 to ${MaxGapMs} ms, or leave it empty for the host pace.`;
  const parallel = props.stage.kind === 'harvest' ? Number(fields.parallel) : undefined;
  if (parallel !== undefined && (!Number.isInteger(parallel) || parallel < 1 || parallel > MaxParallel))
    return `Enter a whole number of parallel fetches from 1 to ${MaxParallel}.`;
  return {
    ...(gap !== undefined ? { gapMs: gap } : {}),
    ...(parallel !== undefined && (parallel > 1 || stored.parallel !== undefined) ? { parallel } : {}),
  };
}

async function save() {
  const api = session.client.value?.pipelineStages;
  if (!api || busy.value) return;
  const fields = draft.value;
  const previous = props.stage.schedule;
  const mode = fields.mode === 'saved' ? 'cron' : fields.mode;
  const cron = fields.mode === 'saved' ? previous?.cron : mode === 'cron' ? calendarCron(fields.calendar) : undefined;
  if (mode === 'cron' && !cron) {
    error.value = 'Choose the calendar days and a time.';
    return;
  }
  const model = selectedModel.value;
  const promptVersion = usesSharedModel.value ? Number(fields.promptVersion) : undefined;
  if (promptVersion !== undefined) {
    const separator = model.indexOf('/');
    if (separator <= 0 || separator === model.length - 1 || /\s/.test(model)) {
      error.value = 'Enter a model route in provider/model format.';
      return;
    }
    if (!Number.isInteger(promptVersion) || promptVersion < 1 || promptVersion > MaxPromptVersion) {
      error.value = `Enter a whole prompt version between 1 and ${MaxPromptVersion}.`;
      return;
    }
  }
  const publishedSinceUtc = fields.publishedSince
    ? fields.publishedSince === props.stage.options.publishedSinceUtc?.slice(0, 10)
      ? props.stage.options.publishedSinceUtc
      : `${fields.publishedSince}T00:00:00Z`
    : undefined;
  const pacing = props.stage.kind === 'process' ? {} : pacingOf(fields);
  if (typeof pacing === 'string') {
    error.value = pacing;
    return;
  }
  const stage: PipelineStageOptions = {
    batchSize: batchSize.value,
    ...(props.stage.kind === 'discover' ? { walk: fields.walk } : {}),
    ...pacing,
    ...(mode === 'cron' && fields.startsOn ? { startsOn: fields.startsOn } : {}),
    ...(props.stage.kind === 'process'
      ? {
          ...(fields.titleContains.trim() ? { titleContains: fields.titleContains.trim() } : {}),
          ...(publishedSinceUtc ? { publishedSinceUtc } : {}),
          ...(fields.processor ? { processor: fields.processor } : {}),
          ...(model ? { model } : {}),
          ...(promptVersion !== undefined ? { promptVersion } : {}),
        }
      : {}),
  };
  const setting: Omit<ScheduleModel, 'id' | 'nextDueAtUtc'> = {
    kind: props.stage.kind,
    mode,
    enabled: fields.enabled && mode !== 'manual',
    timeZone: needsTimeZone.value ? fields.timeZone.trim() : (previous?.timeZone ?? DefaultTimeZone),
    ...(mode === 'interval' || mode === 'adaptive' ? { every: intervalValue(fields.every, previous?.every) } : {}),
    ...(mode === 'adaptive' ? { maxEvery: intervalValue(fields.maxEvery, previous?.maxEvery) } : {}),
    ...(cron ? { cron } : {}),
    ...(previous?.window ? { window: previous.window } : {}),
    stage,
  };
  error.value = '';
  busy.value = true;
  try {
    const result = await api.save(props.pipelineId, setting);
    if (result.ok) {
      open.value = false;
      emit('saved', stage.batchSize);
    } else error.value = result.failure.message || 'Stage settings could not be saved.';
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <Modal :open="open" @update:open="changeOpen">
    <ModalTrigger as-child>
      <Button
        size="xs"
        variant="outline"
        class="min-h-7 max-w-full rounded-full px-3 text-left"
        :is-disabled="isDisabled"
        :aria-label="`Schedule for ${name}`"
      >
        <PillIcon name="clock" />
        <span class="min-w-0 whitespace-normal">{{ scheduleWords(stage.schedule) }}</span>
      </Button>
    </ModalTrigger>
    <ModalContent size="lg">
      <ModalHeader>
        <ModalTitle>{{ name }} schedule</ModalTitle>
        <ModalDescription>Choose when this stage runs.</ModalDescription>
        <ModalClose />
      </ModalHeader>
      <ModalBody>
        <form :id="formId" class="grid gap-4" @submit.prevent="save">
          <div class="grid items-start gap-3 sm:grid-cols-2">
            <div class="grid min-w-0 gap-1.5 text-sm">
              <span>Schedule</span>
              <ChoicePicker v-model="draft.mode" :choices="scheduleChoices" label="Schedule" :is-disabled="busy" />
            </div>
            <label v-if="draft.mode === 'interval' || draft.mode === 'adaptive'" class="grid gap-1.5 text-sm">
              Every ({{ inSeconds ? 'seconds' : 'minutes' }})
              <input v-model.number="draft.every" type="number" :min="everyMinimum" required :class="inputClass" />
            </label>
            <label v-if="draft.mode === 'adaptive'" class="grid gap-1.5 text-sm">
              Maximum interval ({{ inSeconds ? 'seconds' : 'minutes' }})
              <input v-model.number="draft.maxEvery" type="number" :min="draft.every" required :class="inputClass" />
            </label>
          </div>
          <template v-if="draft.mode === 'cron'">
            <div class="grid gap-3 sm:grid-cols-2">
              <div class="grid min-w-0 gap-1.5 text-sm">
                <span>Repeat</span>
                <ChoicePicker v-model="draft.calendar.repeat" :choices="Repeats" label="Repeat" :is-disabled="busy" />
              </div>
              <label class="grid gap-1.5 text-sm">
                Run at
                <input v-model="draft.calendar.time" type="time" required :class="inputClass" />
              </label>
            </div>
            <fieldset v-if="draft.calendar.repeat === 'weekly'" class="min-w-0">
              <legend class="mb-2 text-sm">Days of the week</legend>
              <div class="flex flex-wrap gap-2">
                <label
                  v-for="day in Weekdays"
                  :key="day.value"
                  class="inline-flex cursor-pointer items-center gap-1.5 rounded-md border border-border px-2.5 py-2 text-sm has-checked:border-primary/50 has-checked:bg-primary/10"
                >
                  <input v-model="draft.calendar.weekdays" :value="day.value" type="checkbox" :aria-label="day.name" />
                  {{ day.label }}
                </label>
              </div>
            </fieldset>
            <div v-if="draft.calendar.repeat === 'monthly'" class="grid min-w-0 gap-1.5 text-sm sm:max-w-[50%]">
              <span>Day of each month</span>
              <ChoicePicker
                v-model="draft.calendar.monthDay"
                :choices="MonthDays"
                label="Day of each month"
                :is-disabled="busy"
              />
            </div>
          </template>
          <template v-if="draft.mode !== 'manual'">
            <div v-if="needsTimeZone" class="grid gap-3 sm:grid-cols-2">
              <label v-if="hasCalendar" class="grid gap-1.5 text-sm">
                Start date (optional)
                <input v-model="draft.startsOn" type="date" :class="inputClass" />
              </label>
              <div class="grid min-w-0 gap-1.5 text-sm">
                <span>{{ hasCalendar ? 'Time zone' : 'Time zone for the daily window' }}</span>
                <ChoicePicker
                  v-model="draft.timeZone"
                  :choices="timeZoneChoices"
                  label="Time zone"
                  is-searchable
                  has-trigger-width
                  :is-disabled="busy"
                />
              </div>
            </div>
            <label class="flex items-center gap-2 text-sm">
              <input v-model="draft.enabled" type="checkbox" />
              Enable scheduled runs
            </label>
          </template>
          <fieldset
            v-if="stage.kind !== 'process'"
            class="grid min-w-0 gap-3 border-t border-border pt-3 sm:grid-cols-2"
          >
            <legend class="px-1 text-sm font-medium">Requests</legend>
            <div v-if="stage.kind === 'discover'" class="grid min-w-0 gap-1.5 text-sm sm:col-span-2">
              <span>Walk</span>
              <ChoicePicker v-model="draft.walk" :choices="Walks" label="Walk" :is-disabled="busy" />
            </div>
            <label class="grid gap-1.5 text-sm">
              Gap between requests (ms)
              <input
                v-model.number="draft.gapMs"
                type="number"
                min="0"
                :max="MaxGapMs"
                step="1"
                placeholder="Host pace"
                :disabled="!canSetPacing || busy"
                :class="inputClass"
              />
            </label>
            <label v-if="stage.kind === 'harvest'" class="grid gap-1.5 text-sm">
              Parallel fetches
              <input
                v-model.number="draft.parallel"
                type="number"
                min="1"
                :max="MaxParallel"
                step="1"
                :disabled="!canSetPacing || busy"
                :class="inputClass"
              />
            </label>
            <p class="text-sm text-muted-foreground sm:col-span-2">
              {{
                canSetPacing
                  ? 'An empty gap follows the host pace. A gap of its own replaces the host pace for this stage’s requests; robots delays and refusals still hold the host.'
                  : 'Only an operator sets the gap and the parallel fetches, as they pace a host every tenant shares.'
              }}
            </p>
          </fieldset>
          <fieldset
            v-if="stage.kind === 'process'"
            class="grid min-w-0 gap-3 border-t border-border pt-3 sm:grid-cols-2"
          >
            <legend class="px-1 text-sm font-medium">Processing</legend>
            <div v-if="processorChoices.length" class="grid min-w-0 gap-1.5 text-sm sm:col-span-2">
              <span>Processor</span>
              <ChoicePicker
                v-model="draft.processor"
                :choices="processorChoices"
                label="Processor"
                :is-disabled="busy"
              />
            </div>
            <label class="grid gap-1.5 text-sm">
              Title contains
              <input v-model="draft.titleContains" maxlength="200" :class="inputClass" />
            </label>
            <label class="grid gap-1.5 text-sm">
              Published since (UTC)
              <input v-model="draft.publishedSince" type="date" :class="inputClass" />
            </label>
            <label v-if="showsModel" class="grid gap-1.5 text-sm" :class="{ 'sm:col-span-2': !usesSharedModel }">
              {{ usesSharedModel ? 'Model route' : 'Model' }}
              <input
                v-model="draft.model"
                maxlength="100"
                :placeholder="usesSharedModel ? 'provider/model' : 'Local processor'"
                :required="usesSharedModel"
                :class="inputClass"
              />
            </label>
            <label v-if="usesSharedModel" class="grid gap-1.5 text-sm">
              Prompt version
              <input
                v-model.number="draft.promptVersion"
                type="number"
                min="1"
                :max="MaxPromptVersion"
                step="1"
                required
                :class="inputClass"
              />
            </label>
          </fieldset>
          <p v-if="batchSize < stage.options.batchSize" class="text-sm text-muted-foreground">
            The model will run with a batch of {{ batchSize }} posts.
          </p>
          <p v-if="error" class="text-sm text-destructive" role="alert">{{ error }}</p>
        </form>
      </ModalBody>
      <ModalFooter>
        <Button size="sm" variant="outline" :is-disabled="busy" @click="open = false">Cancel</Button>
        <Button size="sm" type="submit" :form="formId" :is-loading="busy" :is-disabled="busy">Save settings</Button>
      </ModalFooter>
    </ModalContent>
  </Modal>
</template>
