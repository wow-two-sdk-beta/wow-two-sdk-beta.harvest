<script setup lang="ts">
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import type { EntityCalendarEntryModel, EntitySummaryModel, EntityType } from '../../contracts';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { kindGlyphOf } from './PostGlyphs';
import {
  cadenceWords,
  countsText,
  domainWords,
  entityKindWords,
  isoDay,
  monthHeading,
  occurrenceDates,
  occurrenceStatusWords,
  occurrenceTone,
  yearsOf,
} from './EntityWords';
import { dateOf } from './SourceWords';
import { useHarvestConsole } from './HarvestConsole';

/**
 * The entities the open workspace's posts are about — one event across its editions, one listing across its ads — found
 * by name, type and year, each with the years it ran and its predicted next run. The calendar view lists the next
 * twelve months' occurrences by month, predicted ones marked.
 */
defineOptions({ name: 'EntitiesPanel' });
const session = useHarvestConsole();
const { containerId } = session;

type View = 'list' | 'calendar';
const view = ref<View>('list');
const search = ref('');
const query = ref('');
const type = ref<'' | EntityType>('');
const year = ref('');
const entities = shallowRef<readonly EntitySummaryModel[]>([]);
const calendar = shallowRef<readonly EntityCalendarEntryModel[]>([]);
const state = ref<'loading' | 'ready' | 'unavailable'>('loading');
let read: AbortController | undefined;
let typing: ReturnType<typeof setTimeout> | undefined;

const TypeChoices = [
  { value: '', label: 'Any type' },
  { value: 'event', label: 'Events' },
  { value: 'listing', label: 'Listings' },
] as const;
const yearChoices = computed(() => [
  { value: '', label: 'Any year' },
  ...[...new Set([...yearsOf(entities.value).map(String), ...(year.value ? [year.value] : [])])]
    .sort((a, b) => b.localeCompare(a))
    .map((value) => ({ value, label: value })),
]);
const shown = computed(() =>
  year.value
    ? entities.value.filter((entity) => entity.years.some((row) => String(row.year) === year.value))
    : entities.value,
);
const months = computed(() => {
  const groups = new Map<string, EntityCalendarEntryModel[]>();
  for (const entry of calendar.value) {
    const key = entry.occurrence.starts.slice(0, 7);
    groups.set(key, [...(groups.get(key) ?? []), entry]);
  }
  return [...groups.entries()].map(([key, entries]) => ({ key, heading: monthHeading(`${key}-01`), entries }));
});

async function load() {
  read?.abort();
  const controller = new AbortController();
  read = controller;
  state.value = 'loading';
  if (view.value === 'calendar') {
    const from = new Date();
    const to = new Date(from);
    to.setDate(to.getDate() + 365);
    const result = await session.readEntityCalendar(
      { from: isoDay(from), to: isoDay(to), ...(type.value ? { type: type.value } : {}) },
      controller.signal,
    );
    if (controller.signal.aborted) return;
    state.value = result.ok ? 'ready' : 'unavailable';
    if (result.ok) calendar.value = result.value;
    return;
  }
  const result = await session.searchEntities(
    { ...(query.value ? { q: query.value } : {}), ...(type.value ? { type: type.value } : {}), limit: 100 },
    controller.signal,
  );
  if (controller.signal.aborted) return;
  state.value = result.ok ? 'ready' : 'unavailable';
  if (result.ok) entities.value = result.value;
}

watch(search, (value) => {
  if (typing) clearTimeout(typing);
  typing = setTimeout(() => (query.value = value.trim()), 250);
});
watch([containerId, query, type, view], load, { immediate: true });
onScopeDispose(() => {
  read?.abort();
  if (typing) clearTimeout(typing);
});

function placeOf(entityId: string) {
  return { kind: 'entity', id: entityId } as const;
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center gap-2.5">
      <div v-if="view === 'list'" class="min-w-[min(100%,15rem)] flex-1">
        <SearchInput
          v-model="search"
          placeholder="Search entities by name"
          v-bind="{ 'aria-label': 'Search entities', maxlength: 300 }"
          class="[&>input]:rounded-full [&>button]:rounded-full"
          is-clearable
        />
      </div>
      <div class="flex flex-wrap items-center gap-2.5" role="group" aria-label="Filter entities">
        <div class="w-40 max-w-full"><ChoicePicker v-model="type" :choices="TypeChoices" label="Entity type" /></div>
        <div v-if="view === 'list'" class="w-36 max-w-full">
          <ChoicePicker v-model="year" :choices="yearChoices" label="Year" />
        </div>
      </div>
      <div class="ms-auto inline-flex rounded-full border border-border p-0.5" role="group" aria-label="Entities view">
        <Button
          v-for="option in ['list', 'calendar'] as const"
          :key="option"
          size="sm"
          :variant="view === option ? 'solid' : 'ghost'"
          class="rounded-full"
          :aria-pressed="view === option"
          @click="view = option"
        >
          {{ option === 'list' ? 'List' : 'Calendar' }}
        </Button>
      </div>
    </div>

    <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Loading entities…</p>
    <div v-else-if="state === 'unavailable'" class="flex items-center gap-3 text-sm text-muted-foreground">
      <span role="status">Could not load entities.</span>
      <Button variant="outline" size="sm" @click="load">Retry</Button>
    </div>

    <template v-else-if="view === 'list'">
      <p v-if="!shown.length" class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground">
        {{ query || type || year ? 'No entity matches.' : 'No entities yet. They appear once posts are extracted.' }}
      </p>
      <ul v-else class="overflow-hidden rounded-2xl border border-border bg-card" aria-label="Entities">
        <li
          v-for="entity in shown"
          :key="entity.id"
          class="relative grid gap-1.5 border-b border-border px-5 py-3 last:border-b-0 hover:bg-muted/40"
        >
          <div class="flex min-w-0 flex-wrap items-center gap-2">
            <ConsoleLink
              :to="placeOf(entity.id)"
              class="min-w-0 truncate text-[15px] font-semibold outline-none after:absolute after:inset-0 after:content-['']"
            >
              {{ entity.name }}
            </ConsoleLink>
            <HarvestPill tone="info">
              <PillIcon :name="kindGlyphOf(entity.entityType)" />
              {{ entityKindWords(entity.entityType) }}
            </HarvestPill>
            <HarvestPill v-if="entity.primaryDomain" tone="outline" v-bind="{ title: entity.primaryDomain }">
              {{ domainWords(entity.primaryDomain) }}
            </HarvestPill>
            <span class="text-[13px] text-muted-foreground">{{ cadenceWords(entity.cadence) }}</span>
          </div>
          <div class="flex min-w-0 flex-wrap items-center gap-1.5 text-[13px] text-muted-foreground">
            <HarvestPill
              v-for="row in entity.years"
              :key="row.year"
              :tone="row.predicted && !row.occurrences ? 'dashed' : 'neutral'"
              v-bind="{ title: `${row.occurrences} runs · ${row.posts} posts${row.predicted ? ' · predicted' : ''}` }"
            >
              {{ row.year }}
            </HarvestPill>
            <span v-if="entity.predicted">· next {{ dateOf(entity.predicted) }}</span>
            <span>· {{ countsText(entity) }}</span>
          </div>
        </li>
      </ul>
    </template>

    <template v-else>
      <p v-if="!months.length" class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground">
        Nothing on the calendar for the next twelve months.
      </p>
      <section v-for="month in months" :key="month.key" class="grid gap-2" :aria-label="month.heading">
        <h3 class="text-sm font-semibold text-muted-foreground">{{ month.heading }}</h3>
        <ul class="overflow-hidden rounded-2xl border border-border bg-card">
          <li
            v-for="entry in month.entries"
            :key="entry.occurrence.id"
            class="relative flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 border-b border-border px-5 py-2.5 last:border-b-0 hover:bg-muted/40"
          >
            <span class="w-32 shrink-0 text-[13px] tabular-nums">{{ occurrenceDates(entry.occurrence) }}</span>
            <ConsoleLink
              :to="placeOf(entry.entityId)"
              class="min-w-0 flex-1 truncate font-semibold outline-none after:absolute after:inset-0 after:content-['']"
            >
              {{ entry.name }}
            </ConsoleLink>
            <span v-if="entry.occurrence.place?.city" class="text-[13px] text-muted-foreground">
              {{ entry.occurrence.place.city }}
            </span>
            <HarvestPill :tone="occurrenceTone(entry.occurrence)">
              {{ occurrenceStatusWords(entry.occurrence) }}
            </HarvestPill>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>
