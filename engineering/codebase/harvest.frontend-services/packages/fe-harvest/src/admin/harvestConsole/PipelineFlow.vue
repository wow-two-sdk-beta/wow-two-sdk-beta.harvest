<script setup lang="ts">
import { computed, ref } from 'vue';
import {
  Braces,
  CircleAlert,
  CircleCheck,
  CircleMinus,
  Cpu,
  Database,
  FileDown,
  FileJson,
  Globe,
  KeyRound,
  ListFilter,
  LoaderCircle,
  Repeat2,
  Save,
  ScanSearch,
  Sparkles,
  Workflow,
  type LucideIcon,
} from 'lucide-vue-next';
import { Icon } from '@wow-two-beta/fe/foundation/icons';
import type { RunOperationModel } from '../../contracts/PipelineStagesModel';
import HarvestPill from './HarvestPill.vue';

const props = defineProps<{
  operationKeys: readonly string[];
  operations: readonly RunOperationModel[];
  showPending: boolean;
  pendingState: 'planned' | 'pending';
}>();

const operationAppearance: Readonly<Partial<Record<string, { label: string; icon: LucideIcon; color: string }>>> = {
  'prepare-authentication': { label: 'Prepare authentication', icon: KeyRound, color: 'text-warning' },
  fetch: { label: 'Fetch source', icon: Globe, color: 'text-info' },
  'parse-response': { label: 'Parse source response', icon: Braces, color: 'text-warning' },
  'deserialize-response': { label: 'Deserialize source response', icon: FileJson, color: 'text-warning' },
  'inspect-link': { label: 'Check link availability', icon: ScanSearch, color: 'text-info' },
  paginate: { label: 'Find continuation pages', icon: Repeat2, color: 'text-primary' },
  'save-discoveries': { label: 'Save discoveries', icon: Database, color: 'text-success' },
  'normalize-capture': { label: 'Normalize capture', icon: ListFilter, color: 'text-primary' },
  'save-capture': { label: 'Save capture', icon: Save, color: 'text-success' },
  'resolve-model': { label: 'Resolve model', icon: Cpu, color: 'text-info' },
  'load-capture': { label: 'Load saved capture', icon: FileDown, color: 'text-info' },
  'deserialize-capture': { label: 'Deserialize capture', icon: FileJson, color: 'text-warning' },
  'process-capture': { label: 'Process capture', icon: ListFilter, color: 'text-primary' },
  'call-model': { label: 'Call model', icon: Sparkles, color: 'text-primary' },
  'deserialize-result': { label: 'Deserialize model result', icon: FileJson, color: 'text-warning' },
  'save-result': { label: 'Save result', icon: Save, color: 'text-success' },
};
const tones = {
  planned: 'neutral',
  pending: 'neutral',
  running: 'info',
  completed: 'success',
  failed: 'danger',
  skipped: 'neutral',
  interrupted: 'warning',
} as const;
const stateAppearance: Record<keyof typeof tones, { border: string; icon?: LucideIcon; color?: string }> = {
  planned: { border: 'border-border-strong' },
  pending: { border: 'border-border-strong' },
  running: { border: 'border-info', icon: LoaderCircle, color: 'text-info motion-safe:animate-spin' },
  completed: { border: 'border-success/70', icon: CircleCheck, color: 'text-success' },
  failed: { border: 'border-destructive', icon: CircleAlert, color: 'text-destructive' },
  skipped: { border: 'border-border-strong', icon: CircleMinus, color: 'text-muted-foreground' },
  interrupted: { border: 'border-warning', icon: CircleAlert, color: 'text-warning' },
};

const steps = computed(() => {
  const recorded = props.operations.map((operation) => ({
    key: operation.key,
    operation,
    state: operation.result?.status ?? ('running' as const),
  }));
  const pending = props.showPending
    ? props.operationKeys
        .filter((key) => !recorded.some((step) => step.key === key))
        .map((key) => ({ key, operation: undefined, state: props.pendingState }))
    : [];
  const occurrences = new Map<string, number>();
  return [...recorded, ...pending].map((step) => {
    const occurrence = (occurrences.get(step.key) ?? 0) + 1;
    occurrences.set(step.key, occurrence);
    const presentation = operationAppearance[step.key];
    return {
      ...step,
      id: `${step.key}:${occurrence}`,
      label: presentation?.label ?? labelOf(step.key),
      icon: presentation?.icon ?? Workflow,
      color: presentation?.color ?? 'text-muted-foreground',
      indicator: stateAppearance[step.state],
    };
  });
});

const selectedId = ref('');
const selected = computed(
  () =>
    steps.value.find((step) => step.id === selectedId.value) ??
    steps.value.find((step) => step.state === 'running' || step.state === 'failed') ??
    steps.value[0],
);
const result = computed(() => selected.value?.operation?.result);

function labelOf(key: string) {
  const label = key.replace(/[-_]/g, ' ');
  return label.charAt(0).toUpperCase() + label.slice(1);
}

function elapsed(operation?: RunOperationModel) {
  if (!operation?.finishedAtUtc) return '';
  const ms = Math.max(0, Date.parse(operation.finishedAtUtc) - Date.parse(operation.startedAtUtc));
  return ms < 1000 ? `${ms} ms` : `${(ms / 1000).toFixed(1)} s`;
}
</script>

<template>
  <div v-if="selected" class="@container min-w-0 overflow-hidden rounded-xl border border-border">
    <div class="grid min-w-0 @min-[36rem]:grid-cols-[minmax(0,1fr)_minmax(15rem,0.8fr)]">
      <div
        class="grid min-w-0 bg-background/80 p-4 sm:p-5"
        style="
          background-image: radial-gradient(
            circle,
            color-mix(in srgb, currentColor 16%, transparent) 1px,
            transparent 1px
          );
          background-size: 16px 16px;
        "
      >
        <ol aria-label="Pipeline steps" class="grid w-full min-w-0 max-w-80 justify-self-center gap-3">
          <li v-for="(step, index) in steps" :key="step.id" class="relative min-w-0">
            <span
              v-if="index < steps.length - 1"
              aria-hidden="true"
              class="absolute top-10 -bottom-3 left-5 w-0.5 -translate-x-1/2"
              :class="step.state === 'completed' ? 'bg-success/65' : 'bg-border-strong'"
            ></span>
            <button
              type="button"
              class="group relative grid w-full min-w-0 grid-cols-[2.5rem_minmax(0,1fr)] items-start gap-3 rounded-lg text-left focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-ring"
              :aria-pressed="step === selected"
              v-bind="{ 'data-operation': step.key, 'data-state': step.state }"
              @click="selectedId = step.id"
            >
              <span
                class="relative grid size-10 shrink-0 place-items-center rounded-lg border bg-card transition-shadow"
                :class="[
                  step.indicator.border,
                  { 'ring-2 ring-primary/55 ring-offset-2 ring-offset-background': step === selected },
                ]"
              >
                <Icon :icon="step.icon" :size="20" :class="step.color" />
                <span
                  v-if="index > 0"
                  aria-hidden="true"
                  class="absolute top-0 left-1/2 size-2 -translate-1/2 rounded-full border border-border-strong bg-card"
                ></span>
                <span
                  v-if="index < steps.length - 1"
                  aria-hidden="true"
                  class="absolute bottom-0 left-1/2 size-2 -translate-x-1/2 translate-y-1/2 rounded-full border border-border-strong bg-card"
                ></span>
                <span
                  v-if="step.indicator.icon"
                  class="absolute -right-1 -bottom-1 grid size-4 place-items-center rounded-full bg-card"
                >
                  <Icon :icon="step.indicator.icon" :size="14" :class="step.indicator.color" />
                </span>
              </span>
              <span class="min-w-0 self-center">
                <span
                  class="block text-sm font-semibold wrap-anywhere group-hover:text-primary"
                  :class="{ 'text-primary': step === selected }"
                >
                  {{ step.label }}
                </span>
                <span class="block text-xs tabular-nums text-muted-foreground">
                  <span class="capitalize">{{ step.state }}</span>
                  <template v-if="step.operation?.finishedAtUtc">· {{ elapsed(step.operation) }}</template>
                </span>
              </span>
            </button>
          </li>
        </ol>
      </div>
      <aside
        aria-label="Step details"
        class="min-w-0 border-t border-border bg-card/70 p-4 @min-[36rem]:border-t-0 @min-[36rem]:border-l sm:p-5"
      >
        <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1.5">
          <h3 class="text-sm font-semibold wrap-anywhere">{{ selected.label }}</h3>
          <HarvestPill :tone="tones[selected.state]" class="capitalize">{{ selected.state }}</HarvestPill>
        </div>
        <dl
          v-if="
            selected.operation?.finishedAtUtc || result?.input != null || result?.output != null || result?.httpStatus
          "
          class="mt-3 grid grid-cols-[1fr_auto] gap-x-4 gap-y-2 text-sm tabular-nums"
        >
          <template v-if="selected.operation?.finishedAtUtc">
            <dt class="text-muted-foreground">Duration</dt>
            <dd class="text-right">{{ elapsed(selected.operation) }}</dd>
          </template>
          <template v-if="result?.input != null">
            <dt class="text-muted-foreground">Input</dt>
            <dd class="text-right">{{ result.input }}</dd>
          </template>
          <template v-if="result?.output != null">
            <dt class="text-muted-foreground">Output</dt>
            <dd class="text-right">{{ result.output }}</dd>
          </template>
          <template v-if="result?.httpStatus">
            <dt class="text-muted-foreground">Response</dt>
            <dd class="text-right">HTTP {{ result.httpStatus }}</dd>
          </template>
        </dl>
        <p
          v-if="result?.message"
          class="mt-3 text-sm wrap-anywhere"
          :class="selected.state === 'failed' ? 'text-destructive' : 'text-muted-foreground'"
        >
          {{ result.message }}
        </p>
      </aside>
    </div>
  </div>
</template>
