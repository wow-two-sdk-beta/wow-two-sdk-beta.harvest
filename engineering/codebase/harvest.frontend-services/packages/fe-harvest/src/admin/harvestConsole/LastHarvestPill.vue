<script setup lang="ts">
import { computed } from 'vue';
import type { SourceHarvestModel } from '../../contracts';
import HarvestPill from './HarvestPill.vue';
import { clockOf, lastHarvestText, lastHarvestTone } from './SourceWords';

/**
 * The one pill that says what a source's last harvest found: a history icon for `last harvest`, how long ago it ran,
 * the new posts of those found and its outcome when it stopped short — `3 hours ago · +4 new of 23 · partial`. Its
 * title gives the exact time.
 */
defineOptions({ name: 'LastHarvestPill' });
const props = withDefaults(defineProps<{ harvest?: SourceHarvestModel; size?: 'sm' | 'md' }>(), {
  harvest: undefined,
  size: 'sm',
});
const title = computed(() => (props.harvest ? `Last harvest ${clockOf(props.harvest.at)}` : undefined));
</script>

<template>
  <HarvestPill
    :tone="lastHarvestTone(props.harvest)"
    :size="props.size"
    v-bind="{ 'data-pill': 'last-harvest', title }"
  >
    <svg
      width="13"
      height="13"
      viewBox="0 0 24 24"
      class="shrink-0"
      fill="none"
      stroke="currentColor"
      stroke-width="2.2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path d="M3 12a9 9 0 1 0 3-6.7M3 4v5h5M12 7.5V12l3 2" />
    </svg>
    {{ lastHarvestText(props.harvest) }}
  </HarvestPill>
</template>
