<script setup lang="ts">
import { Glyphs, type GlyphName } from './PostGlyphs';

/** A line glyph at the start of a pill; decorative, as the pill's text names what it shows. */
defineOptions({ name: 'PillIcon' });
const props = withDefaults(defineProps<{ name: GlyphName; size?: number }>(), { size: 13 });
</script>

<template>
  <svg
    :width="props.size"
    :height="props.size"
    viewBox="0 0 24 24"
    class="shrink-0"
    fill="none"
    stroke="currentColor"
    stroke-width="2.1"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path :d="Glyphs[props.name]" />
  </svg>
</template>
