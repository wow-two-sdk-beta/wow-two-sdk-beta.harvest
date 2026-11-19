<script setup lang="ts">
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import type { EntityPostModel, EntityTimelineModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { kindGlyphOf } from './PostGlyphs';
import {
  cadenceWords,
  domainWords,
  entityKindWords,
  occurrenceDates,
  occurrenceStatusWords,
  occurrenceTone,
  postsByYear,
  roleTone,
  roleWords,
} from './EntityWords';
import { placeText } from './ProcessedWords';
import { dateOf } from './SourceWords';
import { useHarvestConsole } from './HarvestConsole';

/**
 * One entity: its names, its cadence and predicted next run, then each year it ran, newest first — the occurrences
 * that year with their dates, status and place, and the posts about that year from every source with their roles, each
 * linking to its post page.
 */
defineOptions({ name: 'EntityPanel' });
const props = defineProps<{ entityId: string }>();
const session = useHarvestConsole();

const PostsPage = 200;
const entity = shallowRef<EntityTimelineModel>();
const posts = shallowRef<readonly EntityPostModel[]>([]);
const next = ref<string>();
const state = ref<'loading' | 'ready' | 'unavailable' | 'missing'>('loading');
const postsFailed = ref(false);
const appending = ref(false);
let read: AbortController | undefined;

const predicted = computed(() =>
  entity.value?.years.flatMap((year) => year.occurrences).find((occurrence) => occurrence.predicted),
);
const byYear = computed(() => postsByYear(posts.value));
const years = computed(() => {
  const value = entity.value;
  if (!value) return [];
  const known = new Map(value.years.map((year) => [year.year, year]));
  const all = [...new Set([...known.keys(), ...byYear.value.keys()])].sort((a, b) => b - a);
  return all.map((year) => ({
    year,
    occurrences: known.get(year)?.occurrences ?? [],
    count: known.get(year)?.posts ?? 0,
    posts: byYear.value.get(year) ?? [],
  }));
});

async function load() {
  read?.abort();
  const controller = new AbortController();
  read = controller;
  state.value = 'loading';
  postsFailed.value = false;
  entity.value = undefined;
  posts.value = [];
  next.value = undefined;
  const [timeline, page] = await Promise.all([
    session.readEntity(props.entityId, controller.signal),
    session.readEntityPosts(props.entityId, { limit: PostsPage }, controller.signal),
  ]);
  if (controller.signal.aborted) return;
  if (!timeline.ok) {
    state.value = timeline.failure.status === 404 ? 'missing' : 'unavailable';
    return;
  }
  entity.value = timeline.value;
  if (page.ok) {
    posts.value = page.value.data;
    next.value = page.value.hasMore ? page.value.next : undefined;
  } else postsFailed.value = true;
  state.value = 'ready';
}

async function more() {
  if (!next.value || appending.value) return;
  const controller = read ?? new AbortController();
  appending.value = true;
  const page = await session.readEntityPosts(
    props.entityId,
    { limit: PostsPage, cursor: next.value },
    controller.signal,
  );
  appending.value = false;
  if (controller.signal.aborted || !page.ok) return;
  posts.value = [...posts.value, ...page.value.data];
  next.value = page.value.hasMore ? page.value.next : undefined;
}

watch([() => props.entityId, session.containerId], load, { immediate: true });
onScopeDispose(() => read?.abort());

function postPlace(post: EntityPostModel) {
  return post.sourceId ? ({ kind: 'source', id: post.sourceId, tab: 'posts', post: post.postId } as const) : undefined;
}
</script>

<template>
  <div v-if="entity" class="flex min-w-0 flex-col gap-5">
    <ConsoleLink :to="{ kind: 'entities' }" class="text-sm text-primary hover:underline">← All entities</ConsoleLink>
    <section class="grid gap-3 rounded-2xl border border-border bg-card p-5" aria-label="Entity">
      <div class="flex min-w-0 flex-wrap items-center gap-2">
        <h2 class="min-w-0 text-xl font-semibold">{{ entity.name }}</h2>
        <HarvestPill tone="info">
          <PillIcon :name="kindGlyphOf(entity.entityType)" />
          {{ entityKindWords(entity.entityType) }}
        </HarvestPill>
        <HarvestPill v-if="entity.primaryDomain" tone="outline" v-bind="{ title: entity.primaryDomain }">
          {{ domainWords(entity.primaryDomain) }}
        </HarvestPill>
        <HarvestPill v-if="entity.status !== 'active'" tone="warning">{{ entity.status }}</HarvestPill>
      </div>
      <p v-if="entity.requestedId" class="text-sm text-muted-foreground" role="status">
        The entity asked for was merged into this one.
      </p>
      <p v-if="entity.aliases.length" class="text-sm">
        <span class="text-muted-foreground">Also known as</span>
        {{ entity.aliases.join(' · ') }}
      </p>
      <dl class="grid grid-cols-[repeat(auto-fit,minmax(10rem,1fr))] gap-3 text-sm">
        <div>
          <dt class="text-muted-foreground">Cadence</dt>
          <dd class="font-semibold">
            {{ cadenceWords(entity.cadence) }}
            <span v-if="entity.cadenceConfidence" class="font-normal text-muted-foreground">
              · {{ Math.round(entity.cadenceConfidence * 100) }}% sure
            </span>
          </dd>
        </div>
        <div>
          <dt class="text-muted-foreground">Next run</dt>
          <dd class="font-semibold">{{ predicted ? `${occurrenceDates(predicted)} · predicted` : '—' }}</dd>
        </div>
        <div>
          <dt class="text-muted-foreground">Posts</dt>
          <dd class="font-semibold">
            {{ entity.posts }} from {{ entity.sources }} {{ entity.sources === 1 ? 'source' : 'sources' }}
          </dd>
        </div>
        <div>
          <dt class="text-muted-foreground">Seen</dt>
          <dd class="font-semibold">{{ dateOf(entity.firstSeenAtUtc) }} – {{ dateOf(entity.lastSeenAtUtc) }}</dd>
        </div>
      </dl>
    </section>

    <p v-if="postsFailed" class="text-sm text-muted-foreground" role="status">Could not load this entity's posts.</p>
    <section
      v-for="row in years"
      :key="row.year"
      class="grid gap-3 rounded-2xl border border-border bg-card p-5"
      :aria-label="String(row.year)"
    >
      <h3 class="flex items-baseline gap-2 text-base font-semibold">
        {{ row.year }}
        <span class="text-[13px] font-normal text-muted-foreground">
          {{ row.count }} {{ row.count === 1 ? 'post' : 'posts' }}
        </span>
      </h3>
      <ul v-if="row.occurrences.length" class="grid gap-1.5" aria-label="Occurrences">
        <li
          v-for="occurrence in row.occurrences"
          :key="occurrence.id"
          class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-sm"
        >
          <span class="font-semibold">{{ occurrence.label }}</span>
          <span class="tabular-nums">{{ occurrenceDates(occurrence) }}</span>
          <HarvestPill :tone="occurrenceTone(occurrence)">{{ occurrenceStatusWords(occurrence) }}</HarvestPill>
          <span v-if="occurrence.certainty === 'estimated' && !occurrence.predicted" class="text-muted-foreground">
            estimated
          </span>
          <span v-if="occurrence.place" class="text-muted-foreground">
            {{ placeText(occurrence.place.venue, occurrence.place.city, occurrence.place.country) }}
          </span>
        </li>
      </ul>
      <ul v-if="row.posts.length" class="grid gap-1 border-t border-border pt-3" aria-label="Posts">
        <li
          v-for="post in row.posts"
          :key="post.postId"
          class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-sm"
        >
          <HarvestPill :tone="roleTone(post.role)">{{ roleWords(post) }}</HarvestPill>
          <ConsoleLink
            v-if="postPlace(post)"
            :to="postPlace(post)!"
            class="min-w-0 truncate font-medium hover:underline"
          >
            {{ post.title ?? 'Untitled post' }}
          </ConsoleLink>
          <span v-else class="min-w-0 truncate font-medium">{{ post.title ?? 'Untitled post' }}</span>
          <span class="text-muted-foreground">
            {{ post.source ?? 'Unknown source' }}
            <template v-if="post.postedAt">· {{ dateOf(post.postedAt) }}</template>
          </span>
        </li>
      </ul>
      <p v-else-if="!row.occurrences.length" class="text-sm text-muted-foreground">Nothing recorded this year.</p>
    </section>
    <Button v-if="next" variant="outline" class="self-start" :is-disabled="appending" @click="more">
      {{ appending ? 'Loading…' : 'Show more posts' }}
    </Button>
  </div>
  <p v-else-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Loading the entity…</p>
  <div v-else-if="state === 'unavailable'" class="rounded-2xl border border-border bg-card p-4" role="status">
    <p class="mb-3 text-sm">Could not load this entity.</p>
    <Button variant="outline" @click="load">Retry</Button>
  </div>
  <div v-else class="rounded-2xl border border-border bg-card p-2">
    <EmptyState title="Entity not found" description="This entity is not in the workspace you have open.">
      <template #actions>
        <ConsoleLink :to="{ kind: 'entities' }" class="text-sm text-primary hover:underline">All entities</ConsoleLink>
      </template>
    </EmptyState>
  </div>
</template>
