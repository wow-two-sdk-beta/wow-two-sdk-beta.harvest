<script setup lang="ts">
import { computed } from 'vue';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { languageName } from './SourceSections';
import type { PillTone } from './SourceWords';

defineOptions({ name: 'PostLanguagePill' });
const props = defineProps<{ language: string; tone: PillTone; compact?: boolean }>();
const name = computed(() => languageName(props.language));
</script>

<template>
  <Tooltip :content="name" :open-delay="200">
    <HarvestPill
      :tone="props.tone"
      class="relative z-10 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      v-bind="{ tabindex: 0, 'aria-label': name, 'data-post-language': '' }"
    >
      <PillIcon name="language" />
      {{ (props.compact ? props.language : name).toLowerCase() }}
    </HarvestPill>
  </Tooltip>
</template>
