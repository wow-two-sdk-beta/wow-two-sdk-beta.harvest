<script setup lang="ts">
import { computed } from 'vue';
import { AiBrandMark } from '@wow-two-beta/fe/domain/aiBrands';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import { badgeVariants, BadgeVariant } from '@wow-two-beta/fe/presentation/display';
import { imagePillTintClass } from './PostGalleryStyles';
import { markLabel } from './ProcessedWords';

/**
 * The AI that answered, as its brand mark — Claude, Gemini, OpenAI and the other providers, resolved from the model id
 * first and the provider key next; a model the SDK does not know shows a neutral chip. Hovering or focusing it shows
 * the exact model, cost, latency, tokens and prompt version, one line per answer. `onImage` draws it as glass for a
 * card's image header; `pill` sizes it to the pills it sits among, a round pill as tall as they are.
 */
defineOptions({ name: 'ProviderMark' });
const props = withDefaults(
  defineProps<{
    provider?: string;
    model?: string;
    lines: readonly string[];
    onImage?: boolean;
    pill?: boolean;
    placement?: 'top-end' | 'bottom-start';
  }>(),
  { provider: undefined, model: undefined, onImage: false, pill: false, placement: 'top-end' },
);
const label = computed(() => markLabel(props.provider, props.model));
const glassPill = badgeVariants({
  variant: BadgeVariant.Glass,
  isOutlined: true,
  class: `inline-grid size-6 place-items-center px-0 ${imagePillTintClass}`,
});
</script>

<template>
  <Tooltip :open-delay="150" :placement="props.placement">
    <span
      class="inline-grid shrink-0 place-items-center rounded-full border focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
      :class="[
        props.pill ? 'size-6' : 'size-8 backdrop-blur-md',
        props.onImage
          ? props.pill
            ? glassPill
            : 'border-white/20 bg-black/35 text-white'
          : 'border-border bg-card text-foreground',
      ]"
      tabindex="0"
      role="img"
      :aria-label="label"
      v-bind="props.pill ? { 'data-provider-pill': '' } : {}"
    >
      <AiBrandMark :provider="props.provider" :model="props.model" :size="props.pill ? 14 : 18" />
    </span>
    <template #content>
      <span class="grid gap-0.5 font-mono text-[11px] leading-4 whitespace-nowrap">
        <span class="font-semibold">{{ label }}</span>
        <span v-for="line in props.lines" :key="line">{{ line }}</span>
      </span>
    </template>
  </Tooltip>
</template>
