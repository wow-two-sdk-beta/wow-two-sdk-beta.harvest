<script setup lang="ts">
import { computed, ref } from 'vue';
import { trendDayOf } from './SourceWords';

/** Daily post counts with a full-height hit area and an immediate date and value readout. */
defineOptions({ name: 'NewPostsBars' });
const props = withDefaults(
  defineProps<{
    values: readonly number[];
    height?: number;
    barWidth?: number;
    fill?: boolean;
    max?: number;
    /** Reserves a line above the bars for the readout in a full chart. */
    showReadout?: boolean;
  }>(),
  { height: 28, barWidth: 5, fill: false, max: undefined, showReadout: false },
);
const day = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' });
const top = computed(() => Math.max(1, props.max ?? 0, ...props.values));
const total = computed(() => props.values.reduce((sum, value) => sum + value, 0));
const columns = computed(() =>
  props.values.map((value, index) => ({
    value,
    height: value ? Math.max(3, (value / top.value) * props.height) : 2,
    text: `${day.format(trendDayOf(index, props.values.length))} · ${value} new ${value === 1 ? 'post' : 'posts'}`,
  })),
);
const hovered = ref<number>();
const focused = ref<number>();
const current = computed(() => hovered.value ?? focused.value);
const readout = computed(() => (current.value === undefined ? undefined : columns.value[current.value]?.text));
const tabStop = ref<number>();
const selected = computed(() => Math.min(tabStop.value ?? props.values.length - 1, props.values.length - 1));

function step(event: KeyboardEvent, index: number) {
  const buttons = (event.currentTarget as HTMLElement).parentElement?.querySelectorAll('button');
  const next = event.key === 'ArrowLeft' ? index - 1 : index + 1;
  buttons?.[next]?.focus();
}
</script>

<template>
  <div
    v-if="columns.length"
    class="relative z-10 min-w-0"
    :class="props.fill ? 'w-full' : ''"
    :style="{ width: props.fill ? undefined : `${columns.length * Math.ceil(props.barWidth * 1.6)}px` }"
  >
    <div
      v-if="props.showReadout"
      class="mb-2 min-h-5 text-sm font-medium tabular-nums text-muted-foreground"
      aria-hidden="true"
    >
      {{ readout ?? `${total} new ${total === 1 ? 'post' : 'posts'}` }}
    </div>
    <div
      v-else-if="readout"
      class="pointer-events-none absolute end-0 bottom-full mb-1 rounded-md border border-border bg-popover px-2 py-1 text-xs whitespace-nowrap text-popover-foreground shadow-md"
      aria-hidden="true"
    >
      {{ readout }}
    </div>
    <div
      class="flex min-w-0"
      role="group"
      :aria-label="`New posts over ${props.values.length} days, ${total} in all`"
      @pointerleave="hovered = undefined"
      @keydown.escape.stop="((hovered = undefined), (focused = undefined))"
    >
      <button
        v-for="(column, index) in columns"
        :key="index"
        type="button"
        class="flex min-w-0 flex-1 items-end justify-center rounded-sm focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-ring"
        :style="{ height: `${props.height}px` }"
        :aria-label="column.text"
        :tabindex="selected === index ? 0 : -1"
        @pointerenter="hovered = index"
        @focus="((focused = index), (tabStop = index))"
        @blur="focused = undefined"
        @click="focused = index"
        @keydown.left.prevent="step($event, index)"
        @keydown.right.prevent="step($event, index)"
      >
        <span
          class="w-[64%] rounded-[2px]"
          :class="[column.value ? 'bg-primary' : 'bg-border-strong/60', current === index ? 'ring-1 ring-ring' : '']"
          :style="{ height: `${column.height}px` }"
        ></span>
      </button>
    </div>
  </div>
</template>
