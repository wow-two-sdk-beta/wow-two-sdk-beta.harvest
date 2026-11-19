<script setup lang="ts">
import { computed } from 'vue';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import { providerKindOf, providerName } from './ProcessedWords';

/**
 * The AI provider that answered, as an icon only: an `A` for Anthropic, a knot for OpenAI, a chip for any other.
 * Hovering or focusing it shows the exact model, cost, latency and prompt version, one line per answer.
 */
defineOptions({ name: 'ProviderMark' });
const props = defineProps<{ provider?: string; lines: readonly string[] }>();
const kind = computed(() => providerKindOf(props.provider));
const label = computed(() => `${providerName(props.provider)}: ${props.lines.join('; ')}`);
</script>

<template>
  <Tooltip :open-delay="150" placement="top-end">
    <span
      class="inline-grid size-7 shrink-0 place-items-center rounded-full border border-border bg-card text-foreground"
      tabindex="0"
      role="img"
      :aria-label="label"
      v-bind="{ 'data-provider': kind }"
    >
      <svg width="15" height="15" viewBox="0 0 24 24" aria-hidden="true">
        <path
          v-if="kind === 'anthropic'"
          fill="currentColor"
          d="M13.8 4h-3.1L4.5 20h3.2l1.3-3.4h6.2L16.5 20h3.2zm-3.8 9.9 2.2-5.7 2.2 5.7z"
        />
        <g v-else-if="kind === 'openai'" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round">
          <path d="M12 3.5 19.4 7.8v8.4L12 20.5 4.6 16.2V7.8z" />
          <path d="M12 3.5v8.5l7.4 4.2 M12 12l-7.4 4.2" />
        </g>
        <g v-else fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
          <rect x="6.5" y="6.5" width="11" height="11" rx="2" />
          <path
            d="M9.5 3v3.5 M14.5 3v3.5 M9.5 17.5V21 M14.5 17.5V21 M3 9.5h3.5 M3 14.5h3.5 M17.5 9.5H21 M17.5 14.5H21"
          />
        </g>
      </svg>
    </span>
    <template #content>
      <span class="grid gap-0.5 font-mono text-[11px] leading-4 whitespace-nowrap">
        <span v-for="line in props.lines" :key="line">{{ line }}</span>
      </span>
    </template>
  </Tooltip>
</template>
