<script setup lang="ts">
import { computed } from 'vue';
import HarvestPill from './HarvestPill.vue';
import NewPostsBars from './NewPostsBars.vue';
import SourceIcon from './SourceIcon.vue';
import { groupFamiliesOf, groupHarvestPill, groupTrendOf, type GalleryGroup } from './SourceGroups';
import { plural } from './SourceWords';

/**
 * A publisher at a glance above the sources, as Wheelhouse draws a workspace: its name and source count, its
 * description, how its sources are read, one pill for what their last harvests found and their 14-day chart. The whole
 * card is one button that scrolls to the group's sources below; a group has no page of its own.
 */
defineOptions({ name: 'SourceGroupTile' });
const props = defineProps<{ group: GalleryGroup }>();
const emit = defineEmits<{ open: [] }>();
const pill = computed(() => groupHarvestPill(props.group.sources));
const trend = computed(() => groupTrendOf(props.group.sources));
</script>

<template>
  <article
    class="relative flex min-w-0 flex-col gap-2 rounded-2xl border border-border bg-card p-4 transition-[border-color,box-shadow] duration-150 hover:border-primary/40 hover:shadow-sm has-[button:focus-visible]:outline-2 has-[button:focus-visible]:outline-offset-2 has-[button:focus-visible]:outline-ring motion-reduce:transition-none"
    :aria-label="props.group.name"
  >
    <div class="flex min-w-0 items-baseline gap-2">
      <h3 class="min-w-0 flex-1 truncate text-[15px] font-semibold">
        <button
          type="button"
          class="outline-none after:absolute after:inset-0 after:rounded-2xl after:content-['']"
          :aria-controls="props.group.elementId"
          :title="`Show the sources of ${props.group.name}`"
          @click="emit('open')"
        >
          {{ props.group.name }}
        </button>
      </h3>
      <span class="shrink-0 text-xs tabular-nums text-muted-foreground">
        {{ plural(props.group.sources.length, 'source') }}
      </span>
    </div>
    <p class="line-clamp-2 min-h-[2lh] text-[13px] leading-snug text-muted-foreground">
      {{ props.group.description ?? 'No description yet.' }}
    </p>
    <ul class="flex min-h-7 flex-wrap items-center gap-1.5" aria-label="How its sources are read">
      <li v-for="family in groupFamiliesOf(props.group.sources)" :key="family">
        <SourceIcon :family="family" size="sm" />
      </li>
    </ul>
    <div class="mt-auto flex min-w-0 flex-wrap items-end justify-between gap-x-3 gap-y-2 pt-1">
      <HarvestPill :tone="pill.tone" v-bind="{ 'data-pill': 'group-harvest' }">{{ pill.text }}</HarvestPill>
      <span class="ms-auto flex min-w-12 max-w-36 flex-1 justify-end">
        <NewPostsBars :values="trend" fill />
      </span>
    </div>
  </article>
</template>
