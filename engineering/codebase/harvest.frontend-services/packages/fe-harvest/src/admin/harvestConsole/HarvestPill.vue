<script setup lang="ts">
import { computed } from 'vue';
import type { PillTone } from './SourceWords';

/**
 * One rounded pill: a state, what the last harvest found, a kind or a section. The tone tints it; `dashed` marks an
 * absence, such as `no harvest yet`.
 */
defineOptions({ name: 'HarvestPill' });
const props = withDefaults(defineProps<{ tone?: PillTone; size?: 'sm' | 'md' }>(), { tone: 'neutral', size: 'sm' });
defineSlots<{ default(): unknown }>();

/** Each tone's border, fill and text. @internal */
const ToneClass: Record<PillTone, string> = {
  success: 'border-success/25 bg-success-soft text-success-soft-foreground',
  warning: 'border-warning/30 bg-warning-soft text-warning-soft-foreground',
  danger: 'border-destructive/30 bg-destructive-soft text-destructive-soft-foreground',
  info: 'border-info/25 bg-info-soft text-info-soft-foreground',
  neutral: 'border-transparent bg-muted text-muted-foreground',
  dashed: 'border-dashed border-border-strong bg-transparent text-muted-foreground',
  outline: 'border-border bg-card text-foreground',
  glass: 'border-white/20 bg-white/15 text-white',
  glassSuccess: 'border-emerald-300/30 bg-emerald-400/25 text-emerald-50',
};
const sizeClass = computed(() => (props.size === 'md' ? 'h-7 px-3 text-[13px]' : 'h-6 px-2.5 text-xs'));
</script>

<template>
  <span
    class="inline-flex max-w-full shrink-0 items-center gap-1 truncate rounded-full border font-semibold whitespace-nowrap"
    :class="[ToneClass[props.tone], sizeClass]"
    v-bind="{ 'data-tone': props.tone }"
  >
    <slot></slot>
  </span>
</template>
