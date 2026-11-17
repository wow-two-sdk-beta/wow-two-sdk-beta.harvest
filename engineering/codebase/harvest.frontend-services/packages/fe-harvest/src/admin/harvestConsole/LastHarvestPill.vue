<script setup lang="ts">
import { computed } from 'vue';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceHarvestModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { ageOf, dateTimeOf, lastHarvestNote, lastHarvestTone, statusWords } from './SourceWords';

/** Compact last-harvest age and result, with exact timing and an audit link for incomplete runs. */
defineOptions({ name: 'LastHarvestPill' });
const props = withDefaults(defineProps<{ harvest?: SourceHarvestModel; sourceId?: string; size?: 'sm' | 'md' }>(), {
  harvest: undefined,
  sourceId: undefined,
  size: 'sm',
});
const incomplete = computed(() => !!props.harvest && ['partial', 'failed', 'cancelled'].includes(props.harvest.status));
const text = computed(() => {
  const run = props.harvest;
  if (!run) return 'No harvest yet';
  if (run.status === 'running') return 'Running';
  if (run.status === 'queued') return 'Queued';
  return `${ageOf(run.at, undefined, true)} · ${run.newPosts} new${incomplete.value ? ` · ${statusWords(run.status)}` : ''}`;
});
const hint = computed(() => {
  const run = props.harvest;
  return run
    ? [`Last harvest ${dateTimeOf(run.at)}`, `${run.found} found`, lastHarvestNote(run)].filter(Boolean).join(' · ')
    : 'This source has not been harvested yet';
});
</script>

<template>
  <Tooltip :content="hint" :open-delay="200">
    <ConsoleLink
      v-if="incomplete && props.sourceId && props.harvest"
      :to="{ kind: 'source', id: props.sourceId, run: props.harvest.runId }"
      class="relative z-10 max-w-full rounded-full focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      v-bind="{ 'aria-label': `${text}. Open run audit`, tabindex: 0 }"
    >
      <HarvestPill
        :tone="props.harvest.status === 'failed' ? 'danger' : 'warning'"
        :size="props.size"
        wrap
        v-bind="{ 'data-pill': 'last-harvest' }"
      >
        <PillIcon name="history" />
        <span>{{ text }}</span>
      </HarvestPill>
    </ConsoleLink>
    <HarvestPill
      v-else
      :tone="lastHarvestTone(props.harvest)"
      :size="props.size"
      wrap
      class="relative z-10 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      v-bind="{ 'data-pill': 'last-harvest', tabindex: 0 }"
    >
      <PillIcon name="history" />
      <span>{{ text }}</span>
    </HarvestPill>
  </Tooltip>
</template>
