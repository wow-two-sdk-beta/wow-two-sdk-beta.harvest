<script setup lang="ts">
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import NewPostsBars from './NewPostsBars.vue';
import SourceIcon from './SourceIcon.vue';
import { harvestTimesText, lastHarvestText, lastHarvestTone, StateWords, stateTone } from './SourceWords';

/** A source in one line of its group's list: icon, name, harvest times, the last-harvest pill, its state and trend. */
defineOptions({ name: 'SourceRow' });
const props = defineProps<{ source: SourceOverviewModel; trendMax?: number }>();
</script>

<template>
  <li
    class="relative flex min-h-14 min-w-0 items-center gap-3 border-t border-border px-4 py-2 transition-colors first:border-t-0 hover:bg-muted/40 has-[a:focus-visible]:outline-2 has-[a:focus-visible]:-outline-offset-2 has-[a:focus-visible]:outline-ring"
    :aria-label="props.source.name"
  >
    <SourceIcon :family="props.source.family" size="sm" />
    <ConsoleLink
      :to="{ kind: 'source', id: props.source.sourceId }"
      class="shrink-0 truncate text-sm font-semibold outline-none after:absolute after:inset-0 after:content-[''] max-lg:min-w-0 max-lg:shrink"
    >
      {{ props.source.name }}
    </ConsoleLink>
    <span class="min-w-0 truncate text-sm text-muted-foreground max-md:hidden">
      {{ harvestTimesText(props.source) }}
    </span>
    <span class="ms-auto flex shrink-0 items-center gap-2">
      <HarvestPill
        :tone="lastHarvestTone(props.source.lastHarvest)"
        v-bind="{ 'data-pill': 'last-harvest' }"
        class="max-md:hidden"
      >
        {{ lastHarvestText(props.source.lastHarvest) }}
      </HarvestPill>
      <HarvestPill :tone="stateTone(props.source.state)">{{ StateWords[props.source.state] }}</HarvestPill>
      <span class="flex w-28 justify-end max-sm:hidden">
        <NewPostsBars :values="props.source.newPostsDaily" :max="props.trendMax" fill />
      </span>
    </span>
  </li>
</template>
