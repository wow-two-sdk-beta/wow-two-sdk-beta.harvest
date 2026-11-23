<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { ToggleGroup, ToggleInput } from '@wow-two-beta/fe/presentation/forms';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import { WorkLabels } from './PipelineActivity';
import { isAutomaticSchedule, nextAutomaticSchedule } from './PipelineSchedule';
import PillIcon from './PillIcon.vue';
import SourceIcon from './SourceIcon.vue';
import { sourcePauseReason, sourceStateLabel, sourceTypeLabel } from './SourcePresentation';
import { useHarvestConsole } from './HarvestConsole';
import { sourceProfileSchema } from './SourceProfileSchema';
import {
  ageOf,
  lastHarvestNote,
  lastHarvestTone,
  nextHarvestWords,
  readMethodOf,
  siteUrlOf,
  webUrlOf,
  whenOf,
} from './SourceWords';

/**
 * Names the source above its tabs, as Wheelhouse's product header does: how it is read, its name and state, the last
 * and next harvest, its group, and actions. "Harvest now" queues one harvest per harvested section, for operators only.
 */
defineOptions({ name: 'SourceHeader' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const { pending, canOperate, canWrite } = session;
const now = ref(new Date());
let timer: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
  timer = setInterval(() => (now.value = new Date()), 15_000);
});
onUnmounted(() => clearInterval(timer));

const group = computed(() => session.groupOf(props.source.groupId));
const method = computed(() => readMethodOf(props.source.family));
const pipelines = computed(() =>
  session.pipelines.value.filter((pipeline) => pipeline.sourceId === props.source.sourceId),
);
const sourceState = computed(() => props.source.sourceState ?? session.sourceOf(props.source.sourceId)?.state);
const stateLabel = computed(() =>
  sourceStateLabel({ ...props.source, ...(sourceState.value ? { sourceState: sourceState.value } : {}) }),
);
const readingActive = computed(() => sourceState.value !== 'paused');
const canSetReading = computed(() => canOperate.value && canWrite('sources:manage'));
const readingReason = computed(() => sourcePauseReason(sourceState.value, props.source.sourceState !== undefined));
const site = computed(() => {
  const url = webUrlOf(siteUrlOf(props.source.family, props.source.handle));
  return url && props.source.family === 'site' ? new URL('/', url).href : url;
});
const last = computed(() => props.source.lastHarvest);
const lastText = computed(() => {
  if (!last.value) return 'No harvest yet';
  if (last.value.status === 'running') return 'Running';
  if (last.value.status === 'queued') return 'Queued';
  return ageOf(last.value.at, now.value, true);
});
const lastHint = computed(() => {
  const harvest = last.value;
  if (!harvest) return 'No harvest yet';
  const when = whenOf(harvest.at, now.value);
  if (harvest.status === 'running') return `Harvesting now · queued ${when}`;
  if (harvest.status === 'queued') return `Harvest queued ${when}`;
  return [`Last harvest ${when}`, lastHarvestNote(harvest)].filter(Boolean).join(' · ');
});
const stageSchedules = computed(() =>
  pipelines.value
    .filter((pipeline) => pipeline.state === 'active')
    .flatMap((pipeline) => pipeline.schedules)
    .filter(({ kind }) => kind === 'discover' || kind === 'harvest' || kind === 'process'),
);
const nextStage = computed(() => {
  const next = nextAutomaticSchedule(stageSchedules.value);
  const legacy = props.source.nextHarvestAt;
  return next?.nextDueAtUtc && (!legacy || Date.parse(next.nextDueAtUtc) <= Date.parse(legacy)) ? next : undefined;
});
const nextAt = computed(() =>
  readingActive.value ? (nextStage.value?.nextDueAtUtc ?? props.source.nextHarvestAt) : undefined,
);
const scheduled = computed(
  () => readingActive.value && (!!nextAt.value || stageSchedules.value.some(isAutomaticSchedule)),
);
const nextText = computed(() =>
  scheduled.value && !nextAt.value ? 'Scheduled' : nextHarvestWords(nextAt.value, now.value, true),
);
const nextHint = computed(() => {
  if (!readingActive.value) return 'Automatic runs are paused. Each section’s schedule is preserved.';
  const next = nextAt.value;
  if (!next) return scheduled.value ? 'Automatic runs enabled' : 'No runs scheduled';
  const label = nextStage.value ? `Next ${WorkLabels[nextStage.value.kind].toLowerCase()} run` : 'Next harvest';
  return `${label} · ${whenOf(next, now.value)}`;
});
const fixedPublisher = computed(
  () => sourceProfileSchema(session.capabilities.value[props.source.sourceId]?.configuration.scopeSchema)?.fixedUrl,
);
const canHarvest = computed(
  () => canOperate.value && canWrite('pipelines:run') && props.source.family !== 'instagram' && !fixedPublisher.value,
);
const harvestReason = computed(() => {
  if (!readingActive.value) return 'Enable this source to run a harvest. Section choices are preserved.';
  if (sourceState.value && sourceState.value !== 'active')
    return 'This source is currently unavailable for harvesting.';
  const permission = props.source.permission ?? session.sourceOf(props.source.sourceId)?.permission;
  if (permission && permission !== 'open' && permission !== 'granted')
    return 'Reading permission is required before harvesting this source.';
  if (!pipelines.value.length) return 'Configure a section in Settings before harvesting.';
  if (!pipelines.value.some((pipeline) => pipeline.state !== 'paused')) return 'Enable a section to run a harvest.';
  if (!props.source.sections.some((section) => !section.paused && (section.harvestKind || section.harvesting)))
    return 'Configure a harvest action for a section before running it.';
  return '';
});

function setReading(value: unknown) {
  if (!canSetReading.value || pending.value || readingReason.value) return;
  if ((value === 'enabled' || value === 'disabled') && (value === 'enabled') !== readingActive.value) {
    void session.setSourcePaused(props.source.sourceId, value === 'disabled');
  }
}
</script>

<template>
  <header class="@container/source rounded-2xl border border-border bg-card p-4 sm:p-6" aria-label="Source">
    <div class="grid min-w-0 items-center gap-x-6 gap-y-4 @min-[54rem]/source:grid-cols-[minmax(0,1fr)_auto]">
      <div class="flex min-w-0 items-center gap-4">
        <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="lg" decorative />
        <h1 class="flex min-w-0 flex-wrap items-center gap-2.5 text-2xl font-semibold tracking-tight">
          <span class="min-w-0 wrap-anywhere">{{ props.source.name }}</span>
          <HarvestPill :tone="props.source.state === 'needsAttention' ? 'warning' : 'neutral'">
            {{ stateLabel }}
          </HarvestPill>
        </h1>
      </div>
      <div
        v-if="site || canSetReading || canHarvest"
        class="flex min-w-0 flex-wrap items-center gap-2.5 @max-[54rem]/source:row-start-3 @min-[54rem]/source:justify-end"
        role="group"
        aria-label="Source actions"
      >
        <Button v-if="site" as-child variant="outline">
          <a :href="site" target="_blank" rel="noopener noreferrer">
            {{ method === 'site' ? 'Open the site' : 'Open source' }}
            <PillIcon name="external" />
          </a>
        </Button>
        <Tooltip
          v-if="canSetReading"
          :content="readingReason || 'Pause this source while preserving each section’s settings'"
          :open-delay="200"
        >
          <span
            :tabindex="readingReason ? 0 : undefined"
            :aria-label="readingReason || undefined"
            class="rounded-md focus-visible:outline-2 focus-visible:outline-ring"
          >
            <ToggleGroup
              variant="segmented"
              type="single"
              class="shrink-0"
              :aria-label="`Read ${props.source.name}`"
              :model-value="readingReason ? undefined : readingActive ? 'enabled' : 'disabled'"
              @update:model-value="setReading"
            >
              <ToggleInput value="enabled" variant="soft" size="sm" :is-disabled="pending || !!readingReason">
                Enabled
              </ToggleInput>
              <ToggleInput value="disabled" variant="soft" size="sm" :is-disabled="pending || !!readingReason">
                Disabled
              </ToggleInput>
            </ToggleGroup>
          </span>
        </Tooltip>
        <Tooltip v-if="canHarvest" :content="harvestReason || 'Run the configured harvest now'" :open-delay="200">
          <span
            :tabindex="harvestReason ? 0 : undefined"
            class="rounded-md focus-visible:outline-2 focus-visible:outline-ring"
          >
            <Button
              :is-loading="pending"
              :is-disabled="!!harvestReason"
              @click="session.harvestNow(props.source.sourceId)"
            >
              Harvest now
            </Button>
          </span>
        </Tooltip>
      </div>
      <div
        class="col-span-full flex min-w-0 flex-wrap items-center justify-between gap-x-6 gap-y-3"
        v-bind="{ 'data-source-metadata': '' }"
      >
        <div class="flex min-w-0 flex-wrap items-center gap-2">
          <HarvestPill tone="outline" size="md">
            <PillIcon :name="method" />
            {{ sourceTypeLabel(props.source.family) }}
          </HarvestPill>
          <Tooltip v-if="group" :content="`All sources of ${group.name}`" :open-delay="200">
            <ConsoleLink
              :to="{ kind: 'sources', group: group.id }"
              class="inline-flex min-h-7 min-w-0 max-w-full items-center gap-1.5 rounded-full border border-border bg-card px-3 text-[13px] font-semibold text-muted-foreground hover:border-primary/40 hover:text-foreground"
            >
              <PillIcon name="group" />
              <span class="truncate">{{ group.name }}</span>
            </ConsoleLink>
          </Tooltip>
        </div>
        <div class="flex flex-wrap items-center gap-2" aria-label="Harvest timing">
          <Tooltip :content="lastHint" :open-delay="200">
            <HarvestPill
              :tone="lastHarvestTone(last)"
              size="md"
              v-bind="{ tabindex: 0 }"
              class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              <PillIcon name="history" />
              <span v-if="last" class="sr-only">Last harvest</span>
              {{ lastText }}
            </HarvestPill>
          </Tooltip>
          <Tooltip :content="nextHint" :open-delay="200">
            <HarvestPill
              :tone="scheduled ? 'outline' : 'dashed'"
              size="md"
              v-bind="{ tabindex: 0 }"
              class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
            >
              <PillIcon name="clock" />
              <span class="sr-only">Next scheduled run</span>
              {{ !readingActive ? 'Disabled' : nextText }}
            </HarvestPill>
          </Tooltip>
        </div>
      </div>
    </div>
  </header>
</template>
