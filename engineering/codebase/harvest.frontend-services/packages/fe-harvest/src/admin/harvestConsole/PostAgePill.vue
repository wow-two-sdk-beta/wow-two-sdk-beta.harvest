<script setup lang="ts">
import { computed } from 'vue';
import { FileUp, Import } from 'lucide-vue-next';
import { Icon } from '@wow-two-beta/fe/foundation/icons';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import HarvestPill from './HarvestPill.vue';
import { ageOf, agoOf, type PillTone } from './SourceWords';

const PostDates = {
  posted: { label: 'Posted', icon: FileUp },
  harvested: { label: 'Harvested', icon: Import },
} as const;

defineOptions({ name: 'PostAgePill' });
const props = defineProps<{ at: string; kind: keyof typeof PostDates; tone: PillTone }>();
const presentation = computed(() => PostDates[props.kind]);
const age = computed(() => {
  const now = new Date();
  return { compact: ageOf(props.at, now, true), hint: `${presentation.value.label} ${agoOf(props.at, now)}` };
});
</script>

<template>
  <Tooltip :content="age.hint" :open-delay="200">
    <HarvestPill
      :tone="props.tone"
      class="relative z-10 tabular-nums focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      v-bind="{ tabindex: 0, 'aria-label': age.hint, 'data-post-age': props.kind }"
    >
      <Icon :icon="presentation.icon" :size="12" class="shrink-0" />
      <time :datetime="props.at">{{ age.compact }}</time>
    </HarvestPill>
  </Tooltip>
</template>
