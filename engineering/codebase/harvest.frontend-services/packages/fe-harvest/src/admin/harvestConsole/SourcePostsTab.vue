<script setup lang="ts">
import { computed, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue';
import {
  Carousel,
  CarouselNext,
  CarouselPrev,
  CarouselSlide,
  CarouselSlides,
  CarouselViewport,
  DotsIndicator,
} from '@wow-two-beta/fe/presentation/display';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import type { SourceOverviewModel, SourcePostModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import FilterToggleGroup from './FilterToggleGroup.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import PostPills from './PostPills.vue';
import { kindGlyphOf, sectionGlyphOf } from './PostGlyphs';
import { labelsStatedBy } from './PostLabels';
import SourceIcon from './SourceIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import { languageName } from './SourceSections';
import {
  ageOf,
  agoOf,
  eventDatesOf,
  imageSrcOf,
  kindPlural,
  timingOf,
  webUrlOf,
  type GalleryView,
} from './SourceWords';

/**
 * A source's posts, newest first: a search, filter chips by section and by kind with their counts, and the posts as
 * rows or cards. A card is filled edge to edge by the post's first image, its text at the bottom where the blur
 * fades into the image. The dates and publication age share a line when they fit. A post with no image gets
 * a soft tile with its source's icon. The grid fits as many columns of 17rem or more as the page allows. The pool
 * keeps posts of every kind; a subscriber receives only its matches. A post opens on its own page, with its facts and
 * revisions — revisions never show in the list.
 */
defineOptions({ name: 'SourcePostsTab' });
const props = withDefaults(defineProps<{ source: SourceOverviewModel; view?: GalleryView }>(), { view: 'rows' });
const session = useHarvestConsole();
const posts = shallowRef<readonly SourcePostModel[]>([]);
const state = ref<'loading' | 'ready' | 'unavailable'>('loading');
const search = ref('');
const section = ref<string | null>(null);
const language = ref<string | null>(null);
const kind = ref<string | null>(null);
let read: AbortController | null = null;
let timer: ReturnType<typeof setInterval> | undefined;

async function load() {
  read?.abort();
  const controller = new AbortController();
  read = controller;
  const result = await session.readSourcePosts(props.source.sourceId, controller.signal);
  if (controller.signal.aborted) return;
  if (result.ok) posts.value = result.value;
  if (result.ok || state.value === 'loading') state.value = result.ok ? 'ready' : 'unavailable';
}
/* A source's posts load when it opens, and again after each harvest ends. */
watch(
  () => [props.source.sourceId, props.source.lastHarvest?.runId, props.source.lastHarvest?.status] as const,
  () => void load(),
  { immediate: true },
);
onScopeDispose(() => read?.abort());
onMounted(() => {
  // Media copies can finish independently of the harvest.
  timer = setInterval(() => void load(), 60_000);
});
onScopeDispose(() => clearInterval(timer));

const query = computed(() => search.value.trim().toLowerCase());
const searched = computed(() =>
  posts.value.filter((post) => !query.value || (post.title ?? '').toLowerCase().includes(query.value)),
);
const byPipeline = computed(
  () =>
    new Map(
      [
        ...new Set([
          ...props.source.sections.map((row) => row.pipelineId),
          ...posts.value.flatMap((post) => (post.pipelineId ? [post.pipelineId] : [])),
        ]),
      ].map((id) => [id, session.sectionParts(id)]),
    ),
);
const catalog = computed(() => [
  ...session
    .sectionsOf(props.source.sourceId)
    .map((entry) => ({ name: entry.name ?? entry.label, locale: entry.locale })),
  ...byPipeline.value.values(),
]);
const postSection = (post: SourcePostModel) => (post.pipelineId ? byPipeline.value.get(post.pipelineId) : undefined);
const postLanguage = (post: SourcePostModel) => post.language ?? postSection(post)?.locale;
const matchesSection = (post: SourcePostModel) => !section.value || postSection(post)?.name === section.value;
const matchesLanguage = (post: SourcePostModel) => !language.value || postLanguage(post) === language.value;
const matchesKind = (post: SourcePostModel) => !kind.value || post.kind === kind.value;
const sectionChips = computed(() =>
  [...new Set(catalog.value.map((entry) => entry.name))].map((name) => ({
    key: name,
    label: name,
    icon: sectionGlyphOf(name),
    count: searched.value.filter(
      (post) => postSection(post)?.name === name && matchesKind(post) && matchesLanguage(post),
    ).length,
  })),
);
const languageChips = computed(() => {
  const locales = new Set([
    ...catalog.value.flatMap((entry) => (entry.locale ? [entry.locale] : [])),
    ...posts.value.flatMap((post) => (post.language ? [post.language] : [])),
  ]);
  return [...locales].map((locale) => ({
    key: locale,
    label: languageName(locale),
    icon: 'language' as const,
    code: locale,
    count: searched.value.filter((post) => postLanguage(post) === locale && matchesSection(post) && matchesKind(post))
      .length,
  }));
});
const kindChips = computed(() =>
  [...new Set(posts.value.map((post) => post.kind))].map((value) => ({
    key: value,
    label: kindPlural(value),
    icon: kindGlyphOf(value),
    count: searched.value.filter((post) => post.kind === value && matchesSection(post) && matchesLanguage(post)).length,
  })),
);
const all = computed({
  get: () => (section.value || kind.value || language.value ? null : 'all'),
  set: () => {
    section.value = null;
    language.value = null;
    kind.value = null;
  },
});
/** The card labels the active filters already state, so the cards leave them out. */
const statedLabels = computed(() =>
  labelsStatedBy({
    kind: kind.value ? [kind.value] : [],
    section: section.value ? [section.value] : [],
    language: language.value ? [language.value] : [],
  }),
);
const shown = computed(() =>
  searched.value.filter((post) => matchesSection(post) && matchesLanguage(post) && matchesKind(post)),
);

/** An event's dates — `21 Nov`, `21–23 Nov` — with `ongoing` or `finished` once its stated end allows. */
function startOf(post: SourcePostModel) {
  return (
    [eventDatesOf(post.startsAt, post.endsAt), timingOf(post.startsAt, post.endsAt)].filter(Boolean).join(' · ') ||
    undefined
  );
}
/** An event's dates for a card's chip: the text, and whether it is `ongoing`, `finished` or neither. */
function eventChipOf(post: SourcePostModel) {
  const text = eventDatesOf(post.startsAt, post.endsAt);
  return text ? { text, timing: timingOf(post.startsAt, post.endsAt) } : undefined;
}
/** Why a post has no working link out: it is gone from its source, or it never had a link. */
function unavailableWords(post: SourcePostModel) {
  return post.state === 'gone' ? 'Removed from the source' : 'No link to the original';
}
/** Which image each card's carousel shows, by post. */
const imageIndexes = ref<Record<string, number>>({});
function imageIndexOf(post: SourcePostModel) {
  return imageIndexes.value[post.postId] ?? 0;
}
function showImage(post: SourcePostModel, index: number) {
  imageIndexes.value = { ...imageIndexes.value, [post.postId]: index };
}
/** The page a post opens, `/sources/{id}/posts/{postId}`. */
/**
 * The two arrows of a card's carousel: glass buttons centred on the card's height, shown only while the card is
 * hovered or focused. A one-image card shows the same two, dimmed and inert, so every card reads alike.
 */
const ArrowClass =
  'z-20 size-8 border border-white/20 bg-black/35 text-white ring-0 backdrop-blur-md opacity-0 transition-opacity hover:bg-black/55 group-hover:opacity-100 group-hover:disabled:opacity-40 focus-visible:opacity-100 [@media(hover:none)]:opacity-100 [@media(hover:none)]:disabled:opacity-40';
/** A post's usable images in order: the list's `imageUrls`, else its first image alone. */
function imagesOf(post: SourcePostModel) {
  const urls = post.imageUrls?.length ? post.imageUrls : post.imageUrl ? [post.imageUrl] : [];
  return urls.map((url) => imageSrcOf(url)).filter((src): src is string => !!src);
}
function placeOf(post: SourcePostModel) {
  return { kind: 'source', id: props.source.sourceId, tab: 'posts', post: post.postId } as const;
}
function sectionOf(post: SourcePostModel) {
  return post.pipelineId ? session.sectionLabel(post.pipelineId) : '—';
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center gap-2.5">
      <div class="min-w-[min(100%,15rem)] flex-1">
        <SearchInput
          v-model="search"
          placeholder="Search this source's posts"
          v-bind="{ 'aria-label': 'Search posts' }"
          class="[&>input]:rounded-full [&>button]:rounded-full"
          is-clearable
        />
      </div>
      <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-2" role="group" aria-label="Filter posts">
        <FilterToggleGroup
          v-model="all"
          label="Reset post filters"
          :items="[{ key: 'all', label: 'All', count: searched.length, icon: 'all' }]"
        />
        <FilterToggleGroup v-if="sectionChips.length" v-model="section" label="Section" :items="sectionChips" />
        <FilterToggleGroup v-if="languageChips.length > 1" v-model="language" label="Language" :items="languageChips" />
        <FilterToggleGroup v-if="kindChips.length" v-model="kind" label="Post type" :items="kindChips" />
      </div>
    </div>

    <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Loading posts…</p>
    <p v-else-if="state === 'unavailable'" class="text-sm text-muted-foreground" role="status">
      Could not load this source's posts.
    </p>
    <p
      v-else-if="!shown.length"
      class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground"
    >
      {{ posts.length ? 'No post matches.' : 'No posts yet. They appear after the first harvest.' }}
    </p>
    <div
      v-else-if="props.view === 'rows'"
      class="overflow-hidden rounded-2xl border border-border bg-card"
      role="table"
      aria-label="Posts"
    >
      <div
        role="row"
        class="grid grid-cols-[minmax(0,1fr)_7rem_9rem_8rem] gap-4 border-b border-border px-5 py-2.5 text-[13px] font-semibold text-muted-foreground max-md:grid-cols-[minmax(0,1fr)_6rem]"
      >
        <span role="columnheader">Post</span>
        <span role="columnheader">Kind</span>
        <span role="columnheader" class="max-md:hidden">Section</span>
        <span role="columnheader" class="max-md:hidden">Posted</span>
      </div>
      <div
        v-for="post in shown"
        :key="post.postId"
        role="row"
        class="relative grid min-h-16 grid-cols-[minmax(0,1fr)_7rem_9rem_8rem] items-center gap-4 border-b border-border px-5 py-2.5 last:border-b-0 hover:bg-muted/40 has-[a[data-post-link]:focus-visible]:outline-2 has-[a[data-post-link]:focus-visible]:-outline-offset-2 has-[a[data-post-link]:focus-visible]:outline-ring max-md:grid-cols-[minmax(0,1fr)_6rem]"
      >
        <span role="cell" class="min-w-0">
          <ConsoleLink
            :to="placeOf(post)"
            class="block max-w-full truncate text-start text-[15px] font-semibold outline-none after:absolute after:inset-0 after:content-['']"
            v-bind="{ 'data-post-link': '' }"
          >
            {{ post.title ?? 'Untitled post' }}
          </ConsoleLink>
          <span v-if="startOf(post)" class="block text-[13px] text-muted-foreground">{{ startOf(post) }}</span>
        </span>
        <span role="cell">
          <HarvestPill tone="outline">
            <PillIcon :name="kindGlyphOf(post.kind)" />
            {{ post.kind }}
          </HarvestPill>
        </span>
        <span role="cell" class="truncate max-md:hidden">{{ sectionOf(post) }}</span>
        <span role="cell" class="max-md:hidden">{{ post.postedAt ? agoOf(post.postedAt) : '—' }}</span>
      </div>
    </div>
    <ul v-else class="grid grid-cols-[repeat(auto-fill,minmax(17rem,1fr))] gap-3" aria-label="Posts">
      <li
        v-for="post in shown"
        :key="post.postId"
        class="group relative isolate flex aspect-[4/5] min-w-0 flex-col justify-end overflow-hidden rounded-2xl border border-border bg-muted hover:border-primary/40 has-[a[data-post-link]:focus-visible]:outline-2 has-[a[data-post-link]:focus-visible]:outline-offset-2 has-[a[data-post-link]:focus-visible]:outline-ring"
      >
        <Carousel
          v-if="imagesOf(post).length"
          :index="imageIndexOf(post)"
          :can-loop="imagesOf(post).length > 1"
          class="absolute inset-0 size-full"
          @update:index="showImage(post, $event)"
        >
          <CarouselViewport
            class="size-full rounded-none"
            v-bind="{ tabindex: -1, 'aria-label': 'Photos of this post' }"
          >
            <CarouselSlides class="size-full">
              <CarouselSlide v-for="src in imagesOf(post)" :key="src">
                <img
                  :src="src"
                  alt=""
                  class="size-full object-cover"
                  :class="post.state === 'gone' ? 'grayscale' : ''"
                  loading="lazy"
                  decoding="async"
                  referrerpolicy="no-referrer"
                  v-bind="{ 'data-post-image': '' }"
                />
              </CarouselSlide>
            </CarouselSlides>
          </CarouselViewport>
          <CarouselPrev :class="ArrowClass" class="left-2" />
          <CarouselNext :class="ArrowClass" class="right-2" />
        </Carousel>
        <div
          v-else
          class="absolute inset-0 -z-10 flex items-start justify-center bg-gradient-to-br from-primary-soft to-muted pt-[18%]"
          aria-hidden="true"
        >
          <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="lg" bare />
        </div>
        <div
          v-if="imagesOf(post).length > 1"
          class="pointer-events-none absolute inset-x-0 top-3 z-10 flex h-8 items-center justify-center"
        >
          <DotsIndicator
            :count="imagesOf(post).length"
            :index="imageIndexOf(post)"
            :visible-count="5"
            class="rounded-full bg-black/30 px-2 py-1.5 text-white backdrop-blur-md"
          />
        </div>
        <a
          v-if="webUrlOf(post.url) && post.state !== 'gone'"
          :href="webUrlOf(post.url)"
          target="_blank"
          rel="noopener noreferrer"
          class="absolute top-3 right-3 z-20 inline-flex size-8 items-center justify-center rounded-full border border-white/20 bg-black/35 text-white backdrop-blur-md transition-colors hover:bg-black/55 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
          :aria-label="`Open the original of ${post.title ?? 'this post'}`"
          :title="
            post.state === 'suspect'
              ? 'Open the original — the last check could not confirm it is still there'
              : 'Open the original'
          "
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="M7 17 17 7M8 7h9v9" />
          </svg>
        </a>
        <span
          v-else
          class="absolute top-3 right-3 z-20 inline-flex size-8 items-center justify-center rounded-full border border-dashed border-white/30 bg-black/25 text-white/60 backdrop-blur-md"
          role="img"
          :aria-label="unavailableWords(post)"
          :title="unavailableWords(post)"
        >
          <svg
            width="15"
            height="15"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2.2"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path d="M7 17 17 7M8 7h9v9M4 4l16 16" />
          </svg>
        </span>
        <div
          class="pointer-events-none absolute inset-x-0 bottom-0 h-3/5 bg-gradient-to-t from-black/80 via-black/45 to-transparent backdrop-blur-xl [-webkit-mask-image:linear-gradient(to_top,black_50%,transparent)] [mask-image:linear-gradient(to_top,black_50%,transparent)]"
          aria-hidden="true"
        ></div>
        <div class="z-10 flex min-h-[8.5rem] min-w-0 flex-col gap-2 p-4 text-white">
          <PostPills
            :kind="post.kind"
            :pipeline-id="post.pipelineId"
            :language="post.language"
            :removed="post.state === 'gone'"
            :hidden="statedLabels"
            on-image
            compact
          />
          <ConsoleLink
            :to="placeOf(post)"
            class="line-clamp-2 min-h-[2lh] text-start text-[15px] leading-snug font-semibold outline-none after:absolute after:inset-0 after:content-['']"
            v-bind="{ 'data-post-link': '' }"
          >
            {{ post.title ?? 'Untitled post' }}
          </ConsoleLink>
          <p
            class="mt-auto flex min-h-5 min-w-0 flex-wrap content-start items-start gap-x-3 text-[13px] leading-5 text-white/75"
          >
            <span
              v-if="eventChipOf(post)"
              class="inline-flex min-w-0 max-w-full items-center gap-1"
              :class="
                eventChipOf(post)?.timing === 'ongoing'
                  ? 'text-emerald-200'
                  : eventChipOf(post)?.timing === 'finished'
                    ? 'opacity-60'
                    : ''
              "
              v-bind="{ 'data-event-dates': eventChipOf(post)?.timing ?? '' }"
            >
              <PillIcon name="dates" />
              <span>{{ eventChipOf(post)?.text }}</span>
              <span v-if="eventChipOf(post)?.timing" class="sr-only">{{ eventChipOf(post)?.timing }}</span>
            </span>
            <span v-if="post.postedAt" class="inline-flex shrink-0 items-center gap-1" v-bind="{ 'data-posted': '' }">
              <span class="inline-flex items-center gap-1" aria-hidden="true">
                <PillIcon name="history" />
                {{ ageOf(post.postedAt) }}
              </span>
              <span class="sr-only">posted {{ agoOf(post.postedAt) }}</span>
            </span>
          </p>
        </div>
      </li>
    </ul>
  </div>
</template>
