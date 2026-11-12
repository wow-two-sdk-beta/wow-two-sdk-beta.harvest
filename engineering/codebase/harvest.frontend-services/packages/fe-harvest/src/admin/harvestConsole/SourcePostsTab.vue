<script setup lang="ts">
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue';
import {
  Carousel,
  CarouselNext,
  CarouselPrev,
  CarouselSlide,
  CarouselSlides,
  CarouselViewport,
} from '@wow-two-beta/fe/presentation/display';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import type { SourceOverviewModel, SourcePostModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import FilterChip from './FilterChip.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import PostPills from './PostPills.vue';
import { kindGlyphOf } from './PostGlyphs';
import SourceIcon from './SourceIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import { agoOf, dateOf, imageSrcOf, kindPlural, timingOf, webUrlOf, type GalleryView } from './SourceWords';

/**
 * A source's posts, newest first: a search, filter chips by section and by kind with their counts, and the posts as
 * rows or cards. A card is filled edge to edge by the post's first image, its text on a fixed-height area at the
 * bottom where the blur fades into the image; a post with no image gets a soft tile with its source's icon. Cards
 * fill the width in as many columns of 16rem or more as fit, so the grid steps 4, 3, 2 as the page narrows. The pool
 * keeps posts of every kind; a subscriber receives only its matches. A post opens over the page in a modal with its
 * facts and revisions — revisions never show in the list.
 */
defineOptions({ name: 'SourcePostsTab' });
const props = withDefaults(defineProps<{ source: SourceOverviewModel; view?: GalleryView }>(), { view: 'rows' });
const session = useHarvestConsole();
const posts = shallowRef<readonly SourcePostModel[]>([]);
const state = ref<'loading' | 'ready' | 'unavailable'>('loading');
const search = ref('');
const section = ref<string | null>(null);
const kind = ref<string | null>(null);
let read: AbortController | null = null;

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

const query = computed(() => search.value.trim().toLowerCase());
const searched = computed(() =>
  posts.value.filter((post) => !query.value || (post.title ?? '').toLowerCase().includes(query.value)),
);
const sectionChips = computed(() =>
  [...new Set(searched.value.map((post) => post.pipelineId).filter((id): id is string => !!id))].map((id) => ({
    key: id,
    label: session.sectionLabel(id),
    count: searched.value.filter((post) => post.pipelineId === id && (!kind.value || post.kind === kind.value)).length,
  })),
);
const kindChips = computed(() =>
  [...new Set(searched.value.map((post) => post.kind))].map((value) => ({
    key: value,
    label: kindPlural(value),
    count: searched.value.filter((post) => post.kind === value && (!section.value || post.pipelineId === section.value))
      .length,
  })),
);
const shown = computed(() =>
  searched.value.filter(
    (post) => (!section.value || post.pipelineId === section.value) && (!kind.value || post.kind === kind.value),
  ),
);

/** A post's own date when it has one, with `ongoing` or `finished` once its stated end allows — `1 Oct 2026 · ongoing`. */
function startOf(post: SourcePostModel) {
  return (
    [post.startsAt ? dateOf(post.startsAt) : undefined, timingOf(post.startsAt, post.endsAt)]
      .filter(Boolean)
      .join(' · ') || undefined
  );
}
/** The line under a card's title: how long ago the post itself was published, else its event date — `posted 3 days ago`. */
function cardLine(post: SourcePostModel) {
  return post.postedAt ? `posted ${agoOf(post.postedAt)}` : startOf(post);
}
/** Why a post has no working link out: it is gone from its source, or it never had a link. */
function unavailableWords(post: SourcePostModel) {
  return post.state === 'gone' ? 'Removed from the source' : 'No link to the original';
}
/** The page a post opens, `/sources/{id}/posts/{postId}`. */
/** The two arrows of a card's carousel: glass buttons shown only while the card is hovered or focused. */
const ArrowClass =
  'top-[34%] z-20 size-8 border border-white/20 bg-black/35 text-white ring-0 backdrop-blur-md opacity-0 transition-opacity hover:bg-black/55 group-hover:opacity-100 focus-visible:opacity-100 [@media(hover:none)]:opacity-100';
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
      <div class="min-w-56 flex-1">
        <SearchInput
          v-model="search"
          placeholder="Search this source's posts"
          v-bind="{ 'aria-label': 'Search posts' }"
          is-clearable
        />
      </div>
      <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter posts">
        <FilterChip
          label="All"
          :count="searched.length"
          :is-pressed="!section && !kind"
          @press="((section = null), (kind = null))"
        />
        <FilterChip
          v-for="chip in sectionChips"
          :key="`section:${chip.key}`"
          :label="chip.label"
          :count="chip.count"
          :is-pressed="section === chip.key"
          @press="section = section === chip.key ? null : chip.key"
        />
        <FilterChip
          v-for="chip in kindChips"
          :key="`kind:${chip.key}`"
          :label="chip.label"
          :count="chip.count"
          :is-pressed="kind === chip.key"
          @press="kind = kind === chip.key ? null : chip.key"
        />
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
          <HarvestPill :tone="post.kind === 'event' ? 'success' : 'outline'">
            <PillIcon :name="kindGlyphOf(post.kind)" />
            {{ post.kind }}
          </HarvestPill>
        </span>
        <span role="cell" class="truncate max-md:hidden">{{ sectionOf(post) }}</span>
        <span role="cell" class="max-md:hidden">{{ post.postedAt ? agoOf(post.postedAt) : '—' }}</span>
      </div>
    </div>
    <ul v-else class="grid grid-cols-[repeat(auto-fill,minmax(16rem,1fr))] gap-3" aria-label="Posts">
      <li
        v-for="post in shown"
        :key="post.postId"
        class="group relative isolate flex aspect-[4/5] min-w-0 flex-col justify-end overflow-hidden rounded-2xl border border-border bg-muted hover:border-primary/40 has-[a[data-post-link]:focus-visible]:outline-2 has-[a[data-post-link]:focus-visible]:outline-offset-2 has-[a[data-post-link]:focus-visible]:outline-ring"
      >
        <Carousel v-if="imagesOf(post).length > 1" can-loop class="absolute inset-0 size-full">
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
        <img
          v-else-if="imagesOf(post).length === 1"
          :src="imagesOf(post)[0]"
          alt=""
          class="absolute inset-0 -z-10 size-full object-cover transition-transform duration-300 group-hover:scale-105 motion-reduce:transition-none motion-reduce:group-hover:scale-100"
          :class="post.state === 'gone' ? 'grayscale' : ''"
          loading="lazy"
          decoding="async"
          referrerpolicy="no-referrer"
          v-bind="{ 'data-post-image': '' }"
        />
        <div
          v-else
          class="absolute inset-0 -z-10 flex items-start justify-center bg-gradient-to-br from-primary-soft to-muted pt-[18%]"
          aria-hidden="true"
        >
          <SourceIcon :family="props.source.family" size="lg" bare />
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
        <div class="z-10 flex h-[8.5rem] min-w-0 flex-col gap-2 p-4 text-white">
          <PostPills
            :kind="post.kind"
            :pipeline-id="post.pipelineId"
            :removed="post.state === 'gone'"
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
          <p class="mt-auto truncate text-[13px] text-white/75">{{ cardLine(post) }}</p>
        </div>
      </li>
    </ul>
  </div>
</template>
