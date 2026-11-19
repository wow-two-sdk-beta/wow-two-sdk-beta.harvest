<script setup lang="ts">
import { computed } from 'vue';
import { AiBrandMark } from '@wow-two-beta/fe/domain/aiBrands';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import { markLabel } from './ProcessedWords';

/**
 * The AI that answered, as its brand mark — Claude, Gemini, OpenAI and the other providers, resolved from the model id
 * first and the provider key next; a model the SDK does not know shows a neutral chip. Hovering or focusing it shows
 * the exact model, cost, latency, tokens and prompt version, one line per answer. `onImage` draws it as a glass circle
 * for a card's image header.
 */
defineOptions({ name: 'ProviderMark' });
const props = withDefaults(
  defineProps<{ provider?: string; model?: string; lines: readonly string[]; onImage?: boolean }>(),
  { provider: undefined, model: undefined, onImage: false },
);
const label = computed(() => markLabel(props.provider, props.model));
</script>

<template>
  <Tooltip :open-delay="150" placement="top-end">
    <span
      class="inline-grid size-8 shrink-0 place-items-center rounded-full border backdrop-blur-md focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      :class="props.onImage ? 'border-white/20 bg-black/35 text-white' : 'border-border bg-card text-foreground'"
      tabindex="0"
      role="img"
      :aria-label="label"
    >
      <AiBrandMark :provider="props.provider" :model="props.model" :size="18" />
    </span>
    <template #content>
      <span class="grid gap-0.5 font-mono text-[11px] leading-4 whitespace-nowrap">
        <span class="font-semibold">{{ label }}</span>
        <span v-for="line in props.lines" :key="line">{{ line }}</span>
      </span>
    </template>
  </Tooltip>
</template>
