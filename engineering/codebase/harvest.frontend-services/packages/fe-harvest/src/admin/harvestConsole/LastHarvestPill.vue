<script setup lang="ts">
import { computed } from 'vue';
import type { SourceHarvestModel } from '../../contracts';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { clockOf, lastHarvestNote, lastHarvestText, lastHarvestTone } from './SourceWords';

/**
 * The one pill that says what a source's last harvest found: a history icon for `last harvest`, how long ago it ran,
 * its new posts and the links found — `3 hours ago · +4 new · 23 found`. A partial harvest is told by the amber tint
 * alone, and by a hidden word for assistive technology; the title gives the exact time and the outcome.
 */
defineOptions({ name: 'LastHarvestPill' });
const props = withDefaults(defineProps<{ harvest?: SourceHarvestModel; size?: 'sm' | 'md' }>(), {
  harvest: undefined,
  size: 'sm',
});
const note = computed(() => lastHarvestNote(props.harvest));
const title = computed(() =>
  props.harvest ? [`Last harvest ${clockOf(props.harvest.at)}`, note.value].filter(Boolean).join(' · ') : undefined,
);
</script>

<template>
  <HarvestPill
    :tone="lastHarvestTone(props.harvest)"
    :size="props.size"
    v-bind="{ 'data-pill': 'last-harvest', title }"
  >
    <PillIcon name="history" />
    {{ lastHarvestText(props.harvest) }}
    <span v-if="props.harvest?.status === 'partial'" class="sr-only">, partial</span>
  </HarvestPill>
</template>
