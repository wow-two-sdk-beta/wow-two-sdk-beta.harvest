<script setup lang="ts">
import { ToggleGroup, ToggleInput } from '@wow-two-beta/fe/presentation/forms';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import PillIcon from './PillIcon.vue';
import type { GlyphName } from './PostGlyphs';

defineOptions({ name: 'FilterToggleGroup' });
const props = defineProps<{
  label: string;
  items: readonly {
    key: string;
    label: string;
    count: number;
    icon: GlyphName;
    code?: string;
  }[];
}>();
const value = defineModel<string | null>({ required: true });
</script>

<template>
  <ToggleGroup
    :model-value="value"
    variant="pill"
    class="max-w-full flex-wrap gap-1.5"
    v-bind="{ 'aria-label': props.label }"
    @update:model-value="value = typeof $event === 'string' ? $event : null"
  >
    <Tooltip
      v-for="item in props.items"
      :key="item.key"
      :content="`${item.label} ${item.count}`"
      :is-disabled="value === item.key"
      :open-delay="150"
      placement="bottom"
    >
      <ToggleInput
        :value="item.key"
        variant="solid"
        tone="neutral"
        class="min-h-9 min-w-9 gap-1.5 px-2.5 text-sm"
        :class="{
          '[@media(hover:hover)]:data-[pressed=false]:aspect-square [@media(hover:hover)]:data-[pressed=false]:px-0':
            !item.code,
        }"
        v-bind="{ 'aria-label': `${item.label} ${item.count}` }"
      >
        <PillIcon :name="item.icon" :size="15" />
        <span v-if="item.code" class="text-xs font-semibold uppercase" aria-hidden="true">{{ item.code }}</span>
        <span
          class="inline-flex items-center gap-1.5 whitespace-nowrap"
          :class="value !== item.key && '[@media(hover:hover)]:hidden'"
        >
          {{ item.label }}
          <span class="tabular-nums opacity-75">{{ item.count }}</span>
        </span>
      </ToggleInput>
    </Tooltip>
  </ToggleGroup>
</template>
