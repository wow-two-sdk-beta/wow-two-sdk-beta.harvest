<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import AvailableSourceItem from './AvailableSourceItem.vue';
import { CatalogKinds, catalogMatches } from './AvailableSourceWords';
import { useHarvestConsole } from './HarvestConsole';
import { plural, type GalleryView } from './SourceWords';

/**
 * Collect › Available, for operators: the registered sources this instance has not turned on — never turned on, or
 * turned off — under Events and Listings, as cards or rows, with search and **Turn on** on each. The page reads the
 * instance's catalog when it opens and again after every switch, never on the workspace refresh. The host owns the
 * Cards | Rows choice and passes it as `view`.
 */
defineOptions({ name: 'AvailableSources' });
const props = withDefaults(defineProps<{ view?: GalleryView }>(), { view: 'cards' });
const session = useHarvestConsole();
const { catalog, catalogState, canOperate } = session;
const search = ref('');

const available = computed(() => catalog.value.filter((entry) => entry.state !== 'on'));
const query = computed(() => search.value.trim().toLowerCase());
const matching = computed(() => available.value.filter((entry) => catalogMatches(entry, query.value)));
/** The matching entries under their kind, each section in name order; a kind with none is left out. */
const sections = computed(() =>
  CatalogKinds.map((item) => ({
    ...item,
    entries: matching.value.filter((entry) => entry.kind === item.kind).sort((a, b) => a.name.localeCompare(b.name)),
  })).filter((section) => section.entries.length > 0),
);

onMounted(() => void session.loadCatalog());
</script>

<template>
  <div class="flex flex-col gap-6" role="region" aria-label="Available sources">
    <p class="text-sm text-muted-foreground">Registered sources this instance has not turned on.</p>
    <div v-if="!canOperate" class="rounded-2xl border border-border bg-card p-2">
      <EmptyState
        title="Available is for operators"
        description="An SCFD operator chooses which registered sources this instance uses."
      />
    </div>
    <template v-else>
      <div class="flex flex-wrap items-center gap-2.5">
        <div class="min-w-[min(100%,15rem)] flex-1">
          <SearchInput
            v-model="search"
            :placeholder="`Search ${available.length} sources`"
            v-bind="{ 'aria-label': 'Search available sources' }"
            class="[&>input]:rounded-full [&>button]:rounded-full"
            is-clearable
          />
        </div>
      </div>

      <p v-if="catalogState === 'loading'" class="text-sm text-muted-foreground" role="status">
        Loading available sources…
      </p>
      <div
        v-else-if="catalogState === 'unavailable'"
        class="flex flex-wrap items-center gap-3 rounded-2xl border border-border p-4"
        role="status"
      >
        <p>Could not load the available sources.</p>
        <Button variant="outline" @click="session.loadCatalog">Retry</Button>
      </div>
      <div v-else-if="!available.length" class="rounded-2xl border border-border bg-card p-2">
        <EmptyState
          title="Every registered source is turned on."
          description="A source a later release registers shows up here, off until you turn it on."
        />
      </div>
      <template v-else>
        <div
          v-if="catalogState === 'stale'"
          class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground"
          role="status"
        >
          <p>Showing the last list. The latest refresh failed.</p>
          <Button variant="outline" @click="session.loadCatalog">Retry</Button>
        </div>
        <p v-if="query" class="text-sm text-muted-foreground" role="status">
          {{ matching.length }} of {{ plural(available.length, 'source') }}
        </p>
        <EmptyState v-if="!matching.length" title="No source matches" description="Try another search.">
          <template #actions>
            <Button variant="outline" @click="search = ''">Clear search</Button>
          </template>
        </EmptyState>
        <div v-else class="flex flex-col gap-7">
          <section
            v-for="section in sections"
            :key="section.kind"
            :aria-labelledby="`available-${section.kind}-title`"
            class="min-w-0"
          >
            <header class="mb-3 flex min-w-0 items-center gap-2">
              <h2 :id="`available-${section.kind}-title`" class="truncate text-lg font-semibold">
                {{ section.heading }}
              </h2>
              <span class="shrink-0 text-sm tabular-nums text-muted-foreground">
                {{ plural(section.entries.length, 'source') }}
              </span>
              <span class="h-px min-w-6 flex-1 bg-border" aria-hidden="true"></span>
            </header>
            <ul
              v-if="props.view === 'rows'"
              class="overflow-hidden rounded-2xl border border-border bg-card"
              :aria-label="section.heading"
            >
              <AvailableSourceItem v-for="entry in section.entries" :key="entry.key" :entry="entry" view="rows" />
            </ul>
            <ul v-else class="grid gap-3 md:grid-cols-2 xl:grid-cols-3" :aria-label="section.heading">
              <li v-for="entry in section.entries" :key="entry.key" class="flex min-w-0">
                <AvailableSourceItem class="flex-1" :entry="entry" view="cards" />
              </li>
            </ul>
          </section>
        </div>
      </template>
    </template>
  </div>
</template>
