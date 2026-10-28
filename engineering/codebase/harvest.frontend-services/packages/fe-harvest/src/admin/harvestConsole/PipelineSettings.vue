<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import {
  TextInput,
  NumberInput,
  SelectPicker,
  SelectPickerTrigger,
  SelectPickerValue,
  SelectPickerContent,
  SelectPickerItem,
} from '@wow-two-beta/fe/presentation/forms';
import type { PipelineModel, PipelineUpdate, ScheduleMode, WorkKind, SourceCapabilitiesModel } from '../../contracts';

/** Edits a complete saved target and cadence without resetting drafts during background refresh. */
defineOptions({ name: 'PipelineSettings' });
const props = defineProps<{ pipeline: PipelineModel; pending: boolean; capabilities?: SourceCapabilitiesModel }>();
const emit = defineEmits<{ save: [update: PipelineUpdate] }>();
const name = ref('');
const url = ref('');
const parameters = ref<{ key: string; value: string }[]>([]);
const error = ref('');
interface Cadence {
  kind: WorkKind;
  enabled: boolean;
  mode: ScheduleMode;
  minutes: number;
  maxMinutes: number;
  cron: string;
  timeZone: string;
  start: string;
  end: string;
}
const rows = ref<Cadence[]>([]);
const kinds: WorkKind[] = ['capture', 'discover', 'refresh', 'check', 'metrics'];
const modes = ['interval', 'cron', 'manual', 'adaptive'].map((value) => ({ itemKey: value, value, label: value }));
const descriptions: Partial<Record<WorkKind, string>> = {
  capture: 'Collect new posts',
  discover: 'Find new links',
  refresh: 'Re-read known posts',
  check: 'Check availability',
  metrics: 'Update metrics',
};

function supported(kind: WorkKind) {
  return props.capabilities?.workKinds.includes(kind) ?? true;
}
const urlGuidance = computed(() => {
  const schema = props.capabilities?.configuration.scopeSchema as
    { properties?: { url?: { description?: string } }; required?: string[] } | undefined;
  return (
    schema?.properties?.url?.description ??
    'Copy the filtered source URL, including its query string. The URL must stay on its registered source host.'
  );
});
const urlRequired = computed(() => {
  const required = props.capabilities?.configuration.scopeSchema.required;
  return Array.isArray(required) && required.includes('url');
});

function minutes(value?: string) {
  const match = value?.match(
    /^P(?:(\d+(?:\.\d+)?)D)?(?:T(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?)?$/,
  );
  return match
    ? Number(match[1] ?? 0) * 1440 + Number(match[2] ?? 0) * 60 + Number(match[3] ?? 0) + Number(match[4] ?? 0) / 60
    : 60;
}

function reset() {
  name.value = props.pipeline.name ?? '';
  url.value = props.pipeline.scope.url ?? '';
  parameters.value = Object.entries(props.pipeline.scope.parameters ?? {}).map(([key, value]) => ({ key, value }));
  rows.value = kinds.map((kind) => {
    const existing = props.pipeline.schedules.find((row) => row.kind === kind);
    return {
      kind,
      enabled: existing?.enabled ?? false,
      mode: existing?.mode ?? 'interval',
      minutes: minutes(existing?.every),
      maxMinutes: minutes(existing?.maxEvery ?? 'P1D'),
      cron: existing?.cron ?? '0 9 * * *',
      timeZone: existing?.timeZone ?? 'Asia/Tashkent',
      start: existing?.window?.start ?? '',
      end: existing?.window?.end ?? '',
    };
  });
  error.value = '';
}
watch(() => props.pipeline.id, reset, { immediate: true });

function save() {
  error.value = '';
  if (urlRequired.value && !url.value.trim()) {
    error.value = 'This source adapter requires a starting URL.';
    return;
  }
  if (props.capabilities && rows.value.some((row) => row.enabled && !supported(row.kind))) {
    error.value = 'Disable schedules for operations this source adapter does not support.';
    return;
  }
  const entries = parameters.value.filter((entry) => entry.key.trim());
  if (new Set(entries.map((entry) => entry.key.trim())).size !== entries.length) {
    error.value = 'Each query parameter needs a unique name.';
    return;
  }
  if (rows.value.some((row) => Boolean(row.start) !== Boolean(row.end))) {
    error.value = 'Set both the start and end of an active-hours window.';
    return;
  }
  emit('save', {
    ...(name.value.trim() ? { name: name.value.trim() } : {}),
    scope: {
      ...(url.value.trim() ? { url: url.value.trim() } : {}),
      parameters: Object.fromEntries(entries.map((entry) => [entry.key.trim(), entry.value])),
    },
    schedules: rows.value
      .filter((row) => row.enabled || props.pipeline.schedules.some((existing) => existing.kind === row.kind))
      .map((row) => ({
        kind: row.kind,
        mode: row.mode,
        enabled: row.enabled,
        timeZone: row.timeZone.trim(),
        ...(row.mode === 'interval' || row.mode === 'adaptive' ? { every: `PT${row.minutes}M` } : {}),
        ...(row.mode === 'adaptive' ? { maxEvery: `PT${row.maxMinutes}M` } : {}),
        ...(row.mode === 'cron' ? { cron: row.cron.trim() } : {}),
        ...(row.start && row.end
          ? {
              window: {
                start: row.start.length === 5 ? row.start + ':00' : row.start,
                end: row.end.length === 5 ? row.end + ':00' : row.end,
              },
            }
          : {}),
      })),
  });
}
</script>

<template>
  <form class="grid gap-7 lg:grid-cols-2" aria-label="Pipeline settings" @submit.prevent="save">
    <div class="grid self-start gap-5">
      <label class="block space-y-2 text-sm">
        <span>Name</span>
        <TextInput v-model="name" name="pipeline-name" />
      </label>
      <label class="block space-y-2 text-sm">
        <span>Starting URL / saved filter</span>
        <TextInput
          v-model="url"
          name="pipeline-url"
          placeholder="Paste the complete source URL, including its filters"
        />
      </label>
      <p class="-mt-2 text-xs leading-relaxed text-muted-foreground">
        {{ urlGuidance }}
      </p>
      <details class="rounded-xl border border-border p-4">
        <summary class="cursor-pointer text-sm font-medium">
          Query parameter overrides ({{ parameters.length }})
        </summary>
        <p class="mt-3 text-xs text-muted-foreground">Optional values that replace parameters already in the URL.</p>
        <div v-for="(entry, index) in parameters" :key="index" class="mt-3 flex flex-wrap gap-2">
          <TextInput
            v-model="entry.key"
            :aria-label="'Parameter name ' + (index + 1)"
            placeholder="Parameter name"
            class="min-w-0 flex-1"
          />
          <TextInput
            v-model="entry.value"
            :aria-label="'Parameter value ' + (index + 1)"
            placeholder="Value"
            class="min-w-0 flex-1"
          />
          <Button size="sm" variant="ghost" type="button" @click="parameters.splice(index, 1)">Remove</Button>
        </div>
        <Button class="mt-3" size="sm" variant="outline" type="button" @click="parameters.push({ key: '', value: '' })">
          Add parameter
        </Button>
      </details>
    </div>
    <div>
      <h3 class="font-semibold">Schedules</h3>
      <p class="mt-1 text-xs leading-relaxed text-muted-foreground">
        Each work kind has its own cadence. Disabled schedules allow manual runs. Minimum interval: 5 minutes. Work
        kinds run only when the source adapter supports them.
      </p>
      <div class="mt-4 space-y-3">
        <details
          v-for="row in rows"
          :key="row.kind"
          :open="row.kind === 'capture'"
          class="rounded-xl border border-border p-4"
        >
          <summary class="cursor-pointer text-sm">
            <span class="font-medium capitalize">{{ row.kind }}</span>
            <span class="ml-2 text-xs text-muted-foreground">
              {{ descriptions[row.kind] }} · {{ row.enabled && row.mode !== 'manual' ? row.mode : 'Manual only' }}
            </span>
          </summary>
          <p v-if="!supported(row.kind)" class="mt-3 text-xs text-muted-foreground">
            Not supported by this adapter. Existing schedules can be disabled.
          </p>
          <div class="mt-4 space-y-4">
            <Button
              type="button"
              size="sm"
              :variant="row.enabled ? 'outline' : 'ghost'"
              :aria-pressed="row.enabled"
              :aria-label="'Enable ' + row.kind + ' schedule'"
              :is-disabled="!supported(row.kind) && !row.enabled"
              @click="row.enabled = !row.enabled"
            >
              {{ row.enabled ? 'Schedule enabled' : 'Schedule disabled' }}
            </Button>
            <div class="grid gap-4 sm:grid-cols-2">
              <label class="space-y-2 text-sm">
                <span>Mode</span>
                <SelectPicker v-model="row.mode" :options="modes">
                  <SelectPickerTrigger><SelectPickerValue /></SelectPickerTrigger>
                  <SelectPickerContent>
                    <SelectPickerItem v-for="option in modes" :key="option.itemKey" v-bind="option" />
                  </SelectPickerContent>
                </SelectPicker>
              </label>
              <label class="space-y-2 text-sm">
                <span>Timezone</span>
                <TextInput v-model="row.timeZone" :aria-label="row.kind + ' timezone'" placeholder="Asia/Tashkent" />
              </label>
              <label v-if="row.mode === 'interval' || row.mode === 'adaptive'" class="space-y-2 text-sm">
                <span>Every (minutes)</span>
                <NumberInput
                  v-model="row.minutes"
                  :aria-label="row.kind + ' interval minutes'"
                  :min="5"
                  :max="525600"
                />
              </label>
              <label v-if="row.mode === 'adaptive'" class="space-y-2 text-sm">
                <span>Maximum interval (minutes)</span>
                <NumberInput v-model="row.maxMinutes" :min="row.minutes" :max="525600" />
              </label>
              <label v-if="row.mode === 'cron'" class="space-y-2 text-sm sm:col-span-2">
                <span>Cron expression</span>
                <TextInput v-model="row.cron" :aria-label="row.kind + ' cron'" placeholder="0 9 * * *" />
                <span class="block text-xs text-muted-foreground">
                  0 9 * * * = every day at 09:00 in the selected timezone.
                </span>
              </label>
              <label class="space-y-2 text-sm">
                <span>Active hours start (optional)</span>
                <TextInput v-model="row.start" :aria-label="row.kind + ' window start'" placeholder="HH:mm:ss" />
              </label>
              <label class="space-y-2 text-sm">
                <span>Active hours end (optional)</span>
                <TextInput v-model="row.end" :aria-label="row.kind + ' window end'" placeholder="HH:mm:ss" />
              </label>
            </div>
          </div>
        </details>
      </div>
    </div>
    <p v-if="error" class="text-sm text-destructive lg:col-span-2" role="alert">{{ error }}</p>
    <div class="flex flex-wrap gap-3 lg:col-span-2">
      <Button type="submit" :is-loading="pending">Save pipeline</Button>
      <Button type="button" variant="ghost" :is-disabled="pending" @click="reset">Reset changes</Button>
    </div>
    <p class="text-xs text-muted-foreground lg:col-span-2">
      Changes apply to future runs. Queued runs keep their saved configuration.
    </p>
  </form>
</template>
