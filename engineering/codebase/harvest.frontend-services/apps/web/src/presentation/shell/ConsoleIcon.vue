<script lang="ts">
/** The console's line icons, drawn on a 24-unit grid. */
export const ConsoleIconPaths = {
  home: 'M3 10.5 12 3l9 7.5 M5 9v11h14V9 M10 20v-6h4v6',
  sources: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M3 12h18 M12 3c5 5 5 13 0 18 M12 3c-5 5-5 13 0 18',
  group: 'M4 7h16 M4 12h16 M4 17h16',
  subscribers:
    'M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7 M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6 M16 4.5a3.5 3.5 0 0 1 0 6.5 M18 14.5c1.8.8 3 2.9 3 5.5',
  access: 'M12 3l8 3v6c0 4.5-3.4 8.3-8 9-4.6-.7-8-4.5-8-9V6z M9 12l2 2 4-4',
  panel: 'M3 4h18v16H3z M9 4v16',
  cards: 'M4 4h7v7H4z M13 4h7v7h-7z M4 13h7v7H4z M13 13h7v7h-7z',
  rows: 'M4 6h16 M4 12h16 M4 18h16',
} as const;

export type ConsoleIconName = keyof typeof ConsoleIconPaths;
</script>

<script setup lang="ts">
/** Draws one console line icon at the current text colour. */
defineOptions({ name: 'ConsoleIcon' });
const props = withDefaults(defineProps<{ name: ConsoleIconName; size?: number }>(), { size: 16 });
</script>

<template>
  <svg
    :width="props.size"
    :height="props.size"
    class="shrink-0"
    viewBox="0 0 24 24"
    fill="none"
    stroke="currentColor"
    stroke-width="1.6"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path :d="ConsoleIconPaths[props.name]" />
  </svg>
</template>
