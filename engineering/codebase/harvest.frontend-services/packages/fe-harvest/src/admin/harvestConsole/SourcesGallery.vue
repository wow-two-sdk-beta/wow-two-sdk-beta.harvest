<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import { SearchInput, ToggleInput } from '@wow-two-beta/fe/presentation/forms';
import type { SourceOverviewModel } from '../../contracts';
import FilterToggleGroup from './FilterToggleGroup.vue';
import PillIcon from './PillIcon.vue';
import SourceGroupSection from './SourceGroupSection.vue';
import SourceGroupsDialog from './SourceGroupsDialog.vue';
import SourceGroupTile from './SourceGroupTile.vue';
import { galleryGroupsOf, groupElementId } from './SourceGroups';
import { useHarvestConsole } from './HarvestConsole';
import { useSourcesView } from './UseSourcesView';
import type { GalleryView } from './SourceWords';

/** The state filters, each with the source states it keeps. @internal */
const Filters = [
  { key: 'all', label: 'All', icon: 'all', keeps: () => true },
  {
    key: 'harvesting',
    label: 'Running',
    icon: 'history',
    keeps: (source: SourceOverviewModel) => source.state === 'harvesting',
  },
  {
    key: 'needsAttention',
    label: 'Needs attention',
    icon: 'attention',
    keeps: (source: SourceOverviewModel) => source.state === 'needsAttention',
  },
  { key: 'healthy', label: 'Idle', icon: 'clock', keeps: (source: SourceOverviewModel) => source.state === 'healthy' },
  {
    key: 'idle',
    label: 'Manual or disabled',
    icon: 'section',
    keeps: (source: SourceOverviewModel) => source.state === 'idle',
  },
] as const;

/**
 * The Sources page, built as Wheelhouse's Products page: the groups first — each a tile with its source count,
 * description, read methods, one pill and a 14-day chart, a click scrolling to its sources — then a collapsible section
 * per group, as a three-column grid of cards or a bordered list of rows. Search and the state filters narrow both;
 * a narrowed page keeps only the groups with a match. Sources in no group come last, under `Other`. A page with a single
 * named group keeps its heading above the sources; only a lone ungrouped section omits the separator.
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
const session = useHarvestConsole();
const { overviews, overviewState, sourceListState, groups, sources } = session;
const saved = useSourcesView(session);
const search = computed({ get: () => saved.value.search, set: (value: string) => (saved.value.search = value) });
const filter = computed({ get: () => saved.value.filter, set: (value: string) => (saved.value.filter = value) });
const activeOnly = computed({
  get: () => saved.value.activeOnly,
  set: (value: boolean) => (saved.value.activeOnly = value),
});
const collapsed = computed({
  get: () => saved.value.collapsed,
  set: (value: Set<string>) => (saved.value.collapsed = value),
});
const element = ref<HTMLElement>();
let scroller: HTMLElement | null = null;
let restorePending = true;
let pendingGroup = props.focusGroup;
const groupsOpen = ref(false);
const unavailable = computed(
  () => overviewState.value === 'unavailable' || (activeOnly.value && sourceListState.value === 'unavailable'),
);
const stale = computed(
  () => overviewState.value === 'stale' || (activeOnly.value && sourceListState.value === 'stale'),
);

const active = computed(
  () =>
    new Set(
      sources.value
        .filter(
          (source) => source.state === 'active' && (source.permission === 'open' || source.permission === 'granted'),
        )
        .map((source) => source.id),
    ),
);
const eligible = computed(() =>
  activeOnly.value ? overviews.value.filter((source) => active.value.has(source.sourceId)) : overviews.value,
);
const all = computed(() =>
  galleryGroupsOf(groups.value, eligible.value).filter((group) => !activeOnly.value || group.sources.length > 0),
);
/** Whether one group holds every source, so its summary tile would only repeat the sources. */
const single = computed(() => all.value.length <= 1);
const query = computed(() => search.value.trim().toLowerCase());
/** Whether search or a state filter narrows the page. */
const narrowed = computed(() => query.value !== '' || filter.value !== 'all');
/** The sources the search keeps, before the state filter, so each chip counts what it would show. */
const searched = computed(() =>
  eligible.value.filter((source) => {
    if (!query.value) return true;
    const group = groups.value.find((item) => item.id === source.groupId)?.name ?? '';
    return [source.name, source.handle, group].some((text) => text.toLowerCase().includes(query.value));
  }),
);
const chips = computed(() =>
  Filters.map((item) => ({ ...item, count: searched.value.filter((source) => item.keeps(source)).length })),
);
const kept = computed(() => {
  const keeps = (Filters.find((item) => item.key === filter.value) ?? Filters[0]).keeps;
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

function clearFilters() {
  search.value = '';
  filter.value = 'all';
  activeOnly.value = false;
}

function toggle(key: string) {
  const next = new Set(collapsed.value);
  if (next.has(key)) next.delete(key);
  else next.add(key);
  collapsed.value = next;
}

/** Unfolds a group's section and scrolls to it. */
async function openGroup(key: string, reveal = false) {
  if (reveal) {
    activeOnly.value = false;
    search.value = '';
    filter.value = 'all';
  }
  if (collapsed.value.has(key)) toggle(key);
  await nextTick();
  const heading = document.getElementById(`${groupElementId(key)}-title`);
  heading?.focus({ preventScroll: true });
  heading?.scrollIntoView({
    behavior: window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth',
    block: 'start',
  });
}

/** Restore only on entry or an explicit group navigation; background recovery must preserve browsing. */
async function restoreView() {
  if (!scroller || overviewState.value !== 'ready') return;
  const view = saved.value;
  await nextTick();
  if (saved.value !== view || overviewState.value !== 'ready' || !scroller) return;
  if (pendingGroup) {
    const group = pendingGroup;
    pendingGroup = undefined;
    restorePending = false;
    await openGroup(group, true);
  } else if (restorePending) {
    restorePending = false;
    scroller.scrollTop = view.scrollTop;
  }
}

onMounted(() => {
  let parent = element.value?.parentElement;
  while (parent && !/(auto|scroll)/.test(getComputedStyle(parent).overflowY)) parent = parent.parentElement;
  scroller = parent ?? (document.scrollingElement as HTMLElement | null);
  void restoreView();
});
onBeforeUnmount(() => {
  if (scroller) saved.value.scrollTop = scroller.scrollTop;
});
watch(
  saved,
  (_view, previous) => {
    if (scroller) previous.scrollTop = scroller.scrollTop;
    restorePending = true;
    pendingGroup = props.focusGroup;
    void restoreView();
  },
  { flush: 'sync' },
);
watch(
  () => props.focusGroup,
  (key) => {
    pendingGroup = key;
    void restoreView();
  },
);
watch(overviewState, () => void restoreView());
</script>

<template>
  <div ref="element" class="flex flex-col gap-6" aria-label="Sources" role="region">
    <div class="flex flex-wrap items-center gap-2.5">
      <div class="min-w-[min(100%,15rem)] flex-1">
        <SearchInput
          v-model="search"
          :placeholder="`Search ${eligible.length} sources`"
          v-bind="{ 'aria-label': 'Search sources' }"
          class="[&>input]:rounded-full [&>button]:rounded-full"
          is-clearable
        />
      </div>
      <ToggleInput v-model="activeOnly" variant="solid" class="min-h-9 shrink-0 gap-1.5 rounded-full px-3">
        <PillIcon name="active" :size="15" />
        Active only
      </ToggleInput>
      <FilterToggleGroup
        :model-value="filter"
        label="Filter by state"
        :items="chips"
        @update:model-value="filter = $event ?? 'all'"
      />
      <Button variant="outline" @click="groupsOpen = true">Groups</Button>
    </div>

    <p v-if="overviewState === 'loading'" class="text-sm text-muted-foreground" role="status">Loading sources…</p>
    <div
      v-else-if="unavailable"
      class="flex flex-wrap items-center gap-3 rounded-2xl border border-border p-4"
      role="status"
    >
      <p>Could not load the source list.</p>
      <Button variant="outline" @click="session.refresh">Retry sources</Button>
    </div>
    <div v-else-if="!overviews.length" class="rounded-2xl border border-border bg-card p-2">
      <EmptyState
        title="No sources yet"
        description="Sources arrive with SCFD's readers, one adapter release each; nothing is typed in here."
      />
    </div>
    <template v-else>
      <div v-if="stale" class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground" role="status">
        <p>Showing the last source list. The latest refresh failed.</p>
        <Button variant="outline" @click="session.refresh">Retry sources</Button>
      </div>
      <p class="text-sm text-muted-foreground" role="status">{{ kept.size }} of {{ overviews.length }} sources</p>
      <section v-if="!single" aria-labelledby="source-groups-heading" class="flex min-w-0 flex-col gap-3">
        <h2 id="source-groups-heading" class="sr-only">Groups</h2>
        <ul class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4" aria-labelledby="source-groups-heading">
          <li v-for="entry in shown" :key="entry.group.key" class="flex min-w-0">
            <SourceGroupTile
              class="flex-1"
              :group="entry.group"
              :matching-count="narrowed ? entry.sources.length : undefined"
              @open="openGroup(entry.group.key)"
            />
          </li>
        </ul>
      </section>
      <EmptyState v-if="!shown.length" title="No source matches" description="Try changing the filters.">
        <template #actions>
          <Button variant="outline" @click="clearFilters">Clear filters</Button>
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
          :is-expanded="!collapsed.has(entry.group.key)"
          :is-bare="single && !entry.group.group"
          @toggle="toggle(entry.group.key)"
        />
      </div>
    </template>
    <SourceGroupsDialog v-model:open="groupsOpen" />
  </div>
</template>
