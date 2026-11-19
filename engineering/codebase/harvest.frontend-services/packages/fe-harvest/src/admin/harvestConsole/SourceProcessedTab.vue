<script setup lang="ts">
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type {
  ClassificationVerdict,
  ProcessedEntityType,
  SourceOverviewModel,
  SourceProcessedPostModel,
  SourceProcessedQuery,
} from '../../contracts';
import { SourceProcessedLimits } from '../../contracts/ProcessedModels';
import ChoicePicker from './ChoicePicker.vue';
import HarvestPill from './HarvestPill.vue';
import ProcessedPostCard from './ProcessedPostCard.vue';
import { useHarvestConsole } from './HarvestConsole';

/**
 * What the AI passes made of a source's posts, one card per tagged post, newest first seen first: filters by entity
 * type, verdict and whether an extraction is done, and a cursor page of 30 at a time. The tab label carries the
 * source's total; while a filter narrows the list, a pill first in the filter row reads how many match (`12 of 88`).
 * The Posts tab beside it shows what the source gave, untouched.
 */
defineOptions({ name: 'SourceProcessedTab' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();

type Extracted = '' | 'yes' | 'no';
const entityType = ref<'' | ProcessedEntityType>('');
const verdict = ref<'' | ClassificationVerdict>('');
const extracted = ref<Extracted>('');
const items = shallowRef<readonly SourceProcessedPostModel[]>([]);
const total = ref(0);
const next = ref<string>();
const state = ref<'loading' | 'ready' | 'unavailable'>('loading');
const appending = ref(false);
const appendFailed = ref(false);
const filtered = computed(() => entityType.value !== '' || verdict.value !== '' || extracted.value !== '');
/** The matching count while a filter narrows the list: `12 of 88`, or just the match when the host sends no total. */
const matchText = computed(() => {
  if (!filtered.value) return undefined;
  if (state.value === 'loading') return 'Updating…';
  if (state.value !== 'ready') return undefined;
  const all = props.source.processedCount;
  return all === undefined ? `${total.value} matching` : `${total.value} of ${Math.max(all, total.value)}`;
});
/** The one language every section of the source reads in, so a card names a language only when it differs. */
const uniformLanguage = computed(() => {
  const locales = new Set(props.source.sections.map((section) => session.sectionParts(section.pipelineId).locale));
  const [only] = locales;
  return locales.size === 1 ? only : undefined;
});
let read: AbortController | undefined;

const EntityChoices = [
  { value: '', label: 'Any type' },
  { value: 'event', label: 'Events' },
  { value: 'listing', label: 'Listings' },
  { value: 'none', label: 'Neither' },
  { value: 'unsure', label: 'Unsure' },
] as const;
const VerdictChoices = [
  { value: '', label: 'Any verdict' },
  { value: 'relevant', label: 'Relevant' },
  { value: 'irrelevant', label: 'Irrelevant' },
  { value: 'uncertain', label: 'Uncertain' },
] as const;
const ExtractedChoices = [
  { value: '', label: 'Any extraction' },
  { value: 'yes', label: 'Extracted' },
  { value: 'no', label: 'Not extracted' },
] as const;

function queryOf(cursor?: string): SourceProcessedQuery {
  return {
    ...(entityType.value ? { entityType: entityType.value } : {}),
    ...(verdict.value ? { verdict: verdict.value } : {}),
    ...(extracted.value ? { extracted: extracted.value === 'yes' } : {}),
    limit: SourceProcessedLimits.default,
    ...(cursor ? { cursor } : {}),
  };
}

async function load() {
  read?.abort();
  const controller = new AbortController();
  read = controller;
  state.value = 'loading';
  appendFailed.value = false;
  const result = await session.querySourceProcessed(props.source.sourceId, queryOf(), controller.signal);
  if (controller.signal.aborted) return;
  if (!result.ok) {
    state.value = 'unavailable';
    return;
  }
  items.value = result.value.items;
  total.value = result.value.total;
  next.value = result.value.nextCursor ?? undefined;
  state.value = 'ready';
}

async function more() {
  const cursor = next.value;
  if (!cursor || appending.value) return;
  const controller = read ?? new AbortController();
  appending.value = true;
  appendFailed.value = false;
  const result = await session.querySourceProcessed(props.source.sourceId, queryOf(cursor), controller.signal);
  appending.value = false;
  if (controller.signal.aborted) return;
  if (!result.ok) {
    appendFailed.value = true;
    return;
  }
  const seen = new Set(items.value.map((item) => item.postId));
  items.value = [...items.value, ...result.value.items.filter((item) => !seen.has(item.postId))];
  total.value = result.value.total;
  next.value = result.value.nextCursor ?? undefined;
}

function clear() {
  entityType.value = '';
  verdict.value = '';
  extracted.value = '';
}

watch([() => props.source.sourceId, entityType, verdict, extracted], load, { immediate: true });
onScopeDispose(() => read?.abort());
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center gap-2.5" role="group" aria-label="Filter processed posts">
      <span role="status" aria-live="polite" aria-atomic="true" class="inline-flex empty:-me-2.5">
        <HarvestPill v-if="matchText" tone="neutral" size="md" class="tabular-nums">{{ matchText }}</HarvestPill>
      </span>
      <div class="w-44 max-w-full">
        <ChoicePicker v-model="entityType" :choices="EntityChoices" label="Entity type" />
      </div>
      <div class="w-44 max-w-full"><ChoicePicker v-model="verdict" :choices="VerdictChoices" label="Verdict" /></div>
      <div class="w-44 max-w-full">
        <ChoicePicker v-model="extracted" :choices="ExtractedChoices" label="Extraction" />
      </div>
      <Button v-if="filtered" variant="ghost" size="sm" @click="clear">Clear filters</Button>
    </div>

    <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Loading processed posts…</p>
    <div v-else-if="state === 'unavailable'" class="flex items-center gap-3 text-sm text-muted-foreground">
      <span role="status">Could not load this source's processed posts.</span>
      <Button variant="outline" size="sm" @click="load">Retry</Button>
    </div>
    <p
      v-else-if="!items.length"
      class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground"
    >
      {{ filtered ? 'No processed post matches.' : 'No processed posts yet. Posts appear here once the AI tags them.' }}
    </p>
    <div v-else class="grid gap-3 [grid-template-columns:repeat(auto-fill,minmax(min(100%,24rem),1fr))]">
      <ProcessedPostCard
        v-for="post in items"
        :key="post.postId"
        :source="props.source"
        :post="post"
        :hidden-language="uniformLanguage"
      />
    </div>

    <div v-if="state === 'ready' && (next || appendFailed)" class="flex items-center gap-3">
      <Button variant="outline" :is-disabled="appending" @click="more">
        {{ appending ? 'Loading…' : appendFailed ? 'Retry' : 'Show more' }}
      </Button>
      <span v-if="appendFailed" class="text-sm text-muted-foreground" role="status">Could not load more posts.</span>
    </div>
  </div>
</template>
