<script lang="ts">
import type { ReadMethod } from './SourceWords';

/** The line glyph of each way a source is read, drawn on a 24-unit grid. */
export const ReadMethodPaths: Record<ReadMethod, string> = {
  site: 'M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M12 3a4 9 0 1 0 0 18a4 9 0 1 0 0-18 M3 12h18',
  telegram: 'M21 4 3 11l6 2 2 6 3-4 5 4z m-12 9 8-6',
  instagram:
    'M9 4h6a5 5 0 0 1 5 5v6a5 5 0 0 1-5 5H9a5 5 0 0 1-5-5V9a5 5 0 0 1 5-5z M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8 M17 7h.01',
  social: 'M4 5h16v11H9l-5 4z',
  branch: 'M4 10 12 4l8 6v10H4z M10 20v-6h4v6',
};
</script>

<script setup lang="ts">
import { computed } from 'vue';
import { ReadMethodWords, readMethodOf } from './SourceWords';

/**
 * How a source is read — its website, Telegram, Instagram or a branch — as a glyph on a soft tile. `bare` drops the
 * tile, for a menu row; the title names the method for pointers and assistive technology.
 */
defineOptions({ name: 'SourceIcon' });
const props = withDefaults(defineProps<{ family: string; size?: 'xs' | 'sm' | 'md' | 'lg'; bare?: boolean }>(), {
  size: 'md',
  bare: false,
});
const method = computed(() => readMethodOf(props.family));
/** Each size's tile and glyph. @internal */
const Sizes = {
  xs: { tile: 'size-5 rounded', glyph: 13 },
  sm: { tile: 'size-7 rounded-md', glyph: 15 },
  md: { tile: 'size-9 rounded-lg', glyph: 18 },
  lg: { tile: 'size-14 rounded-xl', glyph: 26 },
} as const;
</script>

<template>
  <span
    class="inline-flex shrink-0 items-center justify-center text-primary"
    :class="props.bare ? '' : ['bg-primary-soft', Sizes[props.size].tile]"
    :title="ReadMethodWords[method]"
    v-bind="{ 'data-read-method': method }"
  >
    <svg
      :width="Sizes[props.size].glyph"
      :height="Sizes[props.size].glyph"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      stroke-width="1.7"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
    >
      <path :d="ReadMethodPaths[method]" />
    </svg>
    <span class="sr-only">{{ ReadMethodWords[method] }}</span>
  </span>
</template>
