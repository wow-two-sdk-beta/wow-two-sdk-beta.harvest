<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import type { SourceOverviewModel } from '../../contracts';
import FilterChip from './FilterChip.vue';
import SourceGroupSection from './SourceGroupSection.vue';
import SourceGroupsDialog from './SourceGroupsDialog.vue';
import SourceGroupTile from './SourceGroupTile.vue';
import { galleryGroupsOf, groupElementId } from './SourceGroups';
import { useHarvestConsole } from './HarvestConsole';
import type { GalleryView } from './SourceWords';

/** The state filters, each with the source states it keeps. @internal */
const Filters = [
  { key: 'all', label: 'All', keeps: () => true },
  {
    key: 'harvesting',
    label: 'Harvesting',
    keeps: (source: SourceOverviewModel) => source.state === 'harvesting' || source.state === 'healthy',
  },
  {
    key: 'needsAttention',
    label: 'Needs attention',
    keeps: (source: SourceOverviewModel) => source.state === 'needsAttention',
  },
  { key: 'idle', label: 'Idle', keeps: (source: SourceOverviewModel) => source.state === 'idle' },
] as const;
type FilterKey = (typeof Filters)[number]['key'];

/**
 * The Sources page, built as Wheelhouse's Products page: the groups first — each a tile with its source count,
 * description, read methods, one pill and a 14-day chart, a click scrolling to its sources — then a collapsible section
 * per group, as a three-column grid of cards or a bordered list of rows. Search and the state filters narrow both;
 * a narrowed page keeps only the groups with a match. Sources in no group come last, under `Other`.
 */
defineOptions({ name: 'SourcesGallery' });
const props = withDefaults(
  defineProps<{
    view?: GalleryView;
    /** A group to unfold and scroll to, by its id or `other`, as the menu's group link names it. */
    focusGroup?: string;
  }>(),
  { view: 'cards', focusGroup: undefined },
);
const { overviews, overviewState, groups } = useHarvestConsole();
const search = ref('');
const filter = ref<FilterKey>('all');
const collapsed = ref(new Set<string>());
const groupsOpen = ref(false);

const all = computed(() => galleryGroupsOf(groups.value, overviews.value));
const query = computed(() => search.value.trim().toLowerCase());
/** Whether search or a state filter narrows the page. */
const narrowed = computed(() => query.value !== '' || filter.value !== 'all');
/** The sources the search keeps, before the state filter, so each chip counts what it would show. */
const searched = computed(() =>
  overviews.value.filter((source) => {
    if (!query.value) return true;
    const group = groups.value.find((item) => item.id === source.groupId)?.name ?? '';
    return [source.name, source.handle, group].some((text) => text.toLowerCase().includes(query.value));
  }),
);
const chips = computed(() =>
  Filters.map((item) => ({ ...item, count: searched.value.filter((source) => item.keeps(source)).length })),
);
const kept = computed(() => {
  const keeps = Filters.find((item) => item.key === filter.value)!.keeps;
  return new Set(searched.value.filter((source) => keeps(source)).map((source) => source.sourceId));
});
/** Each group with the sources it shows; a narrowed page drops the groups with none. */
const shown = computed(() =>
  all.value
    .map((group) => ({ group, sources: group.sources.filter((source) => kept.value.has(source.sourceId)) }))
    .filter((entry) => !narrowed.value || entry.sources.length > 0),
);
/** The busiest day across every source, so a busy source's chart reads taller than a quiet one's. */
const trendMax = computed(() => Math.max(1, ...overviews.value.flatMap((source) => source.newPostsDaily)));

function toggle(key: string) {
  const next = new Set(collapsed.value);
  if (next.has(key)) next.delete(key);
  else next.add(key);
  collapsed.value = next;
}

/** Unfolds a group's section and scrolls to it. */
async function openGroup(key: string) {
  if (collapsed.value.has(key)) toggle(key);
  await nextTick();
  document.getElementById(groupElementId(key))?.scrollIntoView({ behavior: 'smooth', block: 'start' });
}

/* A group named from outside — the menu's group link — opens once its section has drawn. */
watch(
  () => [props.focusGroup, overviewState.value] as const,
  ([key, state]) => {
    if (key && state === 'ready') void openGroup(key);
  },
  { immediate: true, flush: 'post' },
);
</script>

<template>
  <div class="flex flex-col gap-6" aria-label="Sources" role="region">
    <div class="flex flex-wrap items-center gap-2.5">
      <div class="min-w-56 flex-1">
        <SearchInput
          v-model="search"
          :placeholder="`Search ${overviews.length} sources`"
          v-bind="{ 'aria-label': 'Search sources' }"
          is-clearable
        />
      </div>
      <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by state">
        <FilterChip
          v-for="chip in chips"
          :key="chip.key"
          :label="chip.label"
          :count="chip.count"
          :is-pressed="filter === chip.key"
          @press="filter = chip.key"
        />
      </div>
      <Button variant="outline" @click="groupsOpen = true">Groups</Button>
    </div>

    <p v-if="overviewState === 'loading'" class="text-sm text-muted-foreground" role="status">Loading sources…</p>
    <p v-else-if="overviewState === 'unavailable'" class="text-sm text-muted-foreground" role="status">
      Could not load sources. The console retries every few seconds.
    </p>
    <div v-else-if="!overviews.length" class="rounded-2xl border border-border bg-card p-2">
      <EmptyState
        title="No sources yet"
        description="Sources arrive with SCFD's readers, one adapter release each; nothing is typed in here."
      />
    </div>
    <template v-else>
      <section aria-labelledby="source-groups-heading" class="flex min-w-0 flex-col gap-3">
        <h2 id="source-groups-heading" class="sr-only">Groups</h2>
        <ul class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4" aria-labelledby="source-groups-heading">
          <li v-for="entry in shown" :key="entry.group.key" class="flex min-w-0">
            <SourceGroupTile class="flex-1" :group="entry.group" @open="openGroup(entry.group.key)" />
          </li>
        </ul>
      </section>
      <EmptyState v-if="narrowed && !shown.length" title="No source matches" description="Try another search or state.">
        <template #actions>
          <Button variant="outline" @click="((search = ''), (filter = 'all'))">Clear filters</Button>
        </template>
      </EmptyState>
      <div v-else class="flex flex-col gap-7">
        <SourceGroupSection
          v-for="entry in shown"
          :key="entry.group.key"
          :group="entry.group"
          :sources="entry.sources"
          :view="props.view"
          :trend-max="trendMax"
          :is-expanded="narrowed || !collapsed.has(entry.group.key)"
          @toggle="toggle(entry.group.key)"
        />
      </div>
    </template>
    <SourceGroupsDialog v-model:open="groupsOpen" />
  </div>
</template>
