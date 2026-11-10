<script lang="ts">
/** The console's line icons, drawn on a 24-unit grid. */
export const ConsoleIconPaths = {
  home: 'M3 10.5 12 3l9 7.5 M5 9v11h14V9 M10 20v-6h4v6',
  sources: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18 M3 12h18 M12 3c5 5 5 13 0 18 M12 3c-5 5-5 13 0 18',
  pipelines: 'M4 5h6v6H4z M14 13h6v6h-6z M7 11v5h7 M10 8h7v5',
  runs: 'M7 4.5v15l12-7.5z',
  posts: 'M5 3h14v18H5z M8 8h8 M8 12h8 M8 16h5',
  subscriptions: 'M3 5h18v14H3z M3 5l9 8 9-8',
  hosts: 'M4 3h16v7H4z M4 14h16v7H4z M8 6.5h.01 M8 17.5h.01',
  access: 'M12 3l8 3v6c0 4.5-3.4 8.3-8 9-4.6-.7-8-4.5-8-9V6z M9 12l2 2 4-4',
  panel: 'M3 4h18v16H3z M9 4v16',
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
