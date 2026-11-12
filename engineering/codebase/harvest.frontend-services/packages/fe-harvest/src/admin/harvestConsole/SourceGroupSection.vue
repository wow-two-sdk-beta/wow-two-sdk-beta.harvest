<script setup lang="ts">
import type { SourceOverviewModel } from '../../contracts';
import SourceCard from './SourceCard.vue';
import SourceRow from './SourceRow.vue';
import type { GalleryGroup } from './SourceGroups';
import { plural, type GalleryView } from './SourceWords';

/**
 * A group's sources under a minimal separator — its name, how many it shows and a fold control, then a rule — as a
 * three-column grid of cards or one bordered list of rows. The group's figures live on its tile above, not here. With
 * a single group on the page the separator goes too (`isBare`): the sources stand alone, never folded.
 */
defineOptions({ name: 'SourceGroupSection' });
const props = defineProps<{
  group: GalleryGroup;
  /** The sources the search and filter keep, in the group's order. */
  sources: readonly SourceOverviewModel[];
  view: GalleryView;
  isExpanded: boolean;
  /** Draws the sources with no separator and no fold, for a page with only one group. */
  isBare?: boolean;
  trendMax?: number;
}>();
const emit = defineEmits<{ toggle: [] }>();
</script>

<template>
  <section :id="props.group.elementId" :aria-labelledby="`${props.group.elementId}-title`" class="min-w-0 scroll-mt-4">
    <header v-if="!props.isBare" class="mb-3 flex min-w-0 items-center gap-2">
      <button
        type="button"
        class="-ms-1.5 inline-flex min-w-0 items-center gap-2 rounded-md px-1.5 py-1 text-start hover:bg-muted/60 focus-visible:outline-2 focus-visible:outline-ring"
        :aria-expanded="props.isExpanded"
        :aria-controls="`${props.group.elementId}-list`"
        @click="emit('toggle')"
      >
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          class="shrink-0 text-muted-foreground transition-transform duration-150 motion-reduce:transition-none"
          :class="props.isExpanded ? '' : '-rotate-90'"
          aria-hidden="true"
        >
          <path d="M6 9l6 6 6-6" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" />
        </svg>
        <h3 :id="`${props.group.elementId}-title`" class="truncate text-lg font-semibold">{{ props.group.name }}</h3>
        <span class="shrink-0 text-sm tabular-nums text-muted-foreground">
          {{ plural(props.sources.length, 'source') }}
        </span>
      </button>
      <span class="h-px min-w-6 flex-1 bg-border" aria-hidden="true"></span>
    </header>
    <div v-show="props.isBare || props.isExpanded" :id="`${props.group.elementId}-list`">
      <p
        v-if="!props.sources.length"
        class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground"
      >
        No sources in this group yet. File one under it on the source's Settings.
      </p>
      <ul
        v-else-if="props.view === 'rows'"
        class="overflow-hidden rounded-2xl border border-border bg-card"
        :aria-label="props.group.name"
      >
        <SourceRow
          v-for="source in props.sources"
          :key="source.sourceId"
          :source="source"
          :trend-max="props.trendMax"
        />
      </ul>
      <ul v-else class="grid gap-3 md:grid-cols-2 xl:grid-cols-3" :aria-label="props.group.name">
        <li v-for="source in props.sources" :key="source.sourceId" class="flex min-w-0">
          <SourceCard class="flex-1" :source="source" :trend-max="props.trendMax" />
        </li>
      </ul>
    </div>
  </section>
</template>
