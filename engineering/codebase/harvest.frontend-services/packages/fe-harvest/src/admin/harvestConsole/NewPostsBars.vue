<script setup lang="ts">
import { computed } from 'vue';
import { trendDayOf } from './SourceWords';

/**
 * The new posts of the last 14 days as thin columns, oldest first and today last, scaled to the busiest day. An empty
 * day keeps a faint tick so the timeline stays readable; each column's title names its day and count.
 */
defineOptions({ name: 'NewPostsBars' });
const props = withDefaults(
  defineProps<{
    values: readonly number[];
    height?: number;
    barWidth?: number;
    /** Stretches the columns across the container's width. */
    fill?: boolean;
    /** The value a full column stands for, shared by charts a page compares; the series' own top by default. */
    max?: number;
  }>(),
  { height: 28, barWidth: 5, fill: false, max: undefined },
);
const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short' });
const gap = computed(() => Math.max(2, Math.round(props.barWidth * 0.6)));
const width = computed(() => Math.max(0, props.values.length * (props.barWidth + gap.value) - gap.value));
const top = computed(() => Math.max(1, props.max ?? 0, ...props.values));
const total = computed(() => props.values.reduce((sum, value) => sum + value, 0));
const columns = computed(() =>
  props.values.map((value, index) => {
    const height = value > 0 ? Math.max(3, (value / top.value) * props.height) : 0;
    return {
      key: index,
      x: index * (props.barWidth + gap.value),
      y: props.height - height,
      height,
      empty: value === 0,
      title: `${day.format(trendDayOf(index, props.values.length))}: ${value} new ${value === 1 ? 'post' : 'posts'}`,
    };
  }),
);
</script>

<template>
  <svg
    role="img"
    :aria-label="`New posts over ${props.values.length} days: ${total} in all`"
    :width="props.fill ? '100%' : width"
    :height="props.height"
    :viewBox="`0 0 ${width} ${props.height}`"
    :preserveAspectRatio="props.fill ? 'none' : undefined"
    class="shrink-0 overflow-visible"
  >
    <g v-for="column in columns" :key="column.key">
      <title>{{ column.title }}</title>
      <rect :x="column.x - gap / 2" y="0" :width="props.barWidth + gap" :height="props.height" fill="transparent" />
      <rect
        v-if="column.empty"
        :x="column.x"
        :y="props.height - 2"
        :width="props.barWidth"
        height="2"
        rx="1"
        class="fill-border-strong/60"
      />
      <rect
        v-else
        :x="column.x"
        :y="column.y"
        :width="props.barWidth"
        :height="column.height"
        rx="1"
        class="fill-primary"
      />
    </g>
  </svg>
</template>
