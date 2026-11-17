<script setup lang="ts">
import { computed, nextTick, reactive, ref, useTemplateRef } from 'vue';
import { ImageOff, X } from 'lucide-vue-next';
import { Icon } from '@wow-two-beta/fe/foundation/icons';
import {
  Carousel,
  CarouselNext,
  CarouselPrev,
  CarouselSlide,
  CarouselSlides,
  CarouselViewport,
  DotsIndicator,
} from '@wow-two-beta/fe/presentation/display';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import { Popover, PopoverContent, PopoverTrigger, Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel, SourcePostModel } from '../../contracts';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import FilterToggleGroup from './FilterToggleGroup.vue';
import HarvestPill from './HarvestPill.vue';
import ListingCardFacts from './ListingCardFacts.vue';
import PillIcon from './PillIcon.vue';
import PostPills from './PostPills.vue';
import { kindGlyphOf, sectionGlyphOf } from './PostGlyphs';
import { labelsStatedBy } from './PostLabels';
import SourceIcon from './SourceIcon.vue';
import { languageName } from './SourceSections';
import { useHarvestConsole } from './HarvestConsole';
import { useAdaptiveList } from './UseAdaptiveList';
import { useSourcePostPages } from './UseSourcePostPages';
import { ageOf, agoOf, eventDatesOf, imageSrcOf, timingOf, webUrlOf, type GalleryView } from './SourceWords';

/**
 * A source's posts, newest first: a search, filter chips by section and by kind with their counts, and the posts as
 * rows or cards. A card is filled edge to edge by the post's first image, its text at the bottom where the blur
 * fades into the image. The dates and publication age share a line when they fit. Missing images show the source icon
 * and failed images show an unavailable-image icon. The grid fits as many columns of 17rem or more as the page allows. The pool
 * keeps posts of every kind; a subscriber receives only its matches. A post opens on its own page, with its facts and
 * revisions — revisions never show in the list.
 */
defineOptions({ name: 'SourcePostsTab' });
const props = withDefaults(defineProps<{ source: SourceOverviewModel; view?: GalleryView }>(), { view: 'rows' });
const session = useHarvestConsole();
const list = useTemplateRef<HTMLElement>('post-list');
const {
  search,
  filters,
  displayedFilters,
  posts,
  page,
  state,
  browsing,
  updating,
  refreshFailed,
  updatesAvailable,
  appending,
  appendFailed,
  canAppend,
  setFilter,
  clearFilters,
  loadMore,
  retry,
} = useSourcePostPages(() => props.source, list);
const window = useAdaptiveList(list, {
  count: () => posts.value.length,
  grid: () => props.view === 'cards',
  enabled: () => browsing.value,
  canLoad: () => canAppend.value && !appendFailed.value,
  pageSizes: () => ({ minimum: page.value!.defaultPageSize, maximum: page.value!.maxPageSize }),
  load: loadMore,
});
const shown = computed(() => posts.value.slice(window.value.start, window.value.end));
const padding = computed(() => ({
  paddingBlockStart: `${window.value.paddingStart}px`,
  paddingBlockEnd: `${window.value.paddingEnd}px`,
}));
const hasPublisherCategories = computed(() => page.value?.facets.some((facet) => facet.key === 'publisherCategory'));
const inlineKeys = computed(() => [hasPublisherCategories.value ? 'publisherCategory' : 'section', 'language', 'kind']);
const inlineFacets = computed(() =>
  inlineKeys.value.flatMap((key) => {
    const facet = page.value?.facets.find((item) => item.key === key);
    if (!facet?.options.length || (key === 'language' && facet.options.length === 1 && !filters.value.language))
      return [];
    return [
      {
        ...facet,
        items: facet.options.map(({ value, label, count }) => ({
          key: value,
          label,
          count,
          icon:
            key === 'language' ? ('language' as const) : key === 'kind' ? kindGlyphOf(value) : sectionGlyphOf(label),
          ...(key === 'language' ? { code: value } : {}),
        })),
      },
    ];
  }),
);
const moreFacets = computed(
  () => page.value?.facets.filter((facet) => !inlineKeys.value.includes(facet.key) && facet.options.length) ?? [],
);
const moreCount = computed(() => moreFacets.value.filter((facet) => filters.value[facet.key]).length);
const selectedMore = computed(() =>
  moreFacets.value.flatMap((facet) => {
    const value = filters.value[facet.key];
    if (!value) return [];
    return [
      {
        key: facet.key,
        label: facet.label,
        value: facet.options.find((option) => option.value === value)?.label ?? value,
      },
    ];
  }),
);
const uniformLanguage = computed(() => {
  const options = page.value?.facets
    .find((facet) => facet.key === 'language')
    ?.options.filter((option) => option.count > 0);
  return options?.length === 1 && options[0]!.count === page.value?.total ? options[0]!.value : undefined;
});
const all = computed({
  get: () => (Object.keys(filters.value).length ? null : 'all'),
  set: clearFilters,
});
const statedLabels = computed(() =>
  labelsStatedBy({
    kind: displayedFilters.value.kind ? [displayedFilters.value.kind] : [],
    section: (hasPublisherCategories.value ? displayedFilters.value.publisherCategory : displayedFilters.value.section)
      ? ['selected']
      : [],
    language: uniformLanguage.value
      ? [uniformLanguage.value]
      : displayedFilters.value.language
        ? [displayedFilters.value.language]
        : [],
    state: displayedFilters.value.availability === 'gone' ? ['removed'] : [],
  }),
);

async function showMore(event: MouseEvent) {
  const start = posts.value.length;
  if ((await loadMore(undefined, true)) && event.detail === 0) {
    await nextTick();
    list.value?.querySelector<HTMLElement>(`[data-post-index="${start}"] [data-post-link]`)?.focus();
  }
}

/** An event's dates — `21 Nov`, `21–23 Nov` — with `ongoing` or `finished` once its stated end allows. */
function startOf(post: SourcePostModel) {
  return [eventDatesOf(post.startsAt, post.endsAt), eventStatusOf(post)].filter(Boolean).join(' · ') || undefined;
}
/** An event's dates for a card's chip: the text, and whether it is `ongoing`, `finished` or neither. */
function eventChipOf(post: SourcePostModel) {
  const text = eventDatesOf(post.startsAt, post.endsAt);
  return text ? { text, timing: eventStatusOf(post) } : undefined;
}

function eventStatusOf(post: SourcePostModel) {
  if (!post.eventTiming) return timingOf(post.startsAt, post.endsAt);
  if (post.eventTiming === 'endUnknown') return 'end unknown';
  return post.eventTiming === 'ongoing' || post.eventTiming === 'finished' ? post.eventTiming : undefined;
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
/** Image navigation appears when a card has multiple photos and receives pointer or keyboard attention. */
const ArrowClass =
  'z-20 size-8 border border-white/20 bg-black/35 text-white ring-0 backdrop-blur-md opacity-0 transition-opacity hover:bg-black/55 group-hover/post-card:opacity-100 group-focus-within/post-card:opacity-100 [@media(hover:none)]:opacity-100';
const FallbackTileClass = 'absolute inset-0 grid place-items-center bg-gradient-to-br from-primary-soft to-muted';
// Retain a failed image's fallback when virtualization remounts the card.
const failedImages = reactive(new Set<string>());
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
          v-bind="{ 'aria-label': 'Search posts', maxlength: 200 }"
          class="[&>input]:rounded-full [&>button]:rounded-full"
          is-clearable
        />
      </div>
      <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-2" role="group" aria-label="Filter posts">
        <FilterToggleGroup
          v-model="all"
          label="Reset post filters"
          :items="[{ key: 'all', label: 'All', count: page?.searchTotal ?? 0, icon: 'all' }]"
        />
        <FilterToggleGroup
          v-for="facet in inlineFacets"
          :key="facet.key"
          :model-value="filters[facet.key] ?? null"
          :label="facet.label"
          :items="facet.items"
          :class="facet.key === 'kind' ? 'border-s border-border ps-3' : ''"
          @update:model-value="setFilter(facet.key, $event)"
        />
        <Popover v-if="moreFacets.length" placement="bottom-end">
          <PopoverTrigger as-child>
            <Button variant="outline" class="min-h-9 rounded-full">
              <PillIcon name="all" />
              Filters
              <span v-if="moreCount">{{ moreCount }}</span>
            </Button>
          </PopoverTrigger>
          <PopoverContent class="grid w-64 max-w-[calc(100vw-2.5rem)] gap-3">
            <div v-for="facet in moreFacets" :key="facet.key" class="grid min-w-0 gap-1 text-sm font-medium">
              <span>{{ facet.label }}</span>
              <ChoicePicker
                :model-value="filters[facet.key] ?? ''"
                :choices="[
                  { value: '', label: 'Any' },
                  ...facet.options.map(({ value, label, count }) => ({ value, label: `${label} (${count})` })),
                ]"
                :label="facet.label"
                has-trigger-width
                :is-searchable="facet.options.length > 12"
                @update:model-value="setFilter(facet.key, $event || null)"
              />
            </div>
          </PopoverContent>
        </Popover>
      </div>
    </div>

    <div v-if="page" class="flex min-h-7 flex-wrap items-center gap-x-3 gap-y-2 text-sm text-muted-foreground">
      <span role="status" aria-live="polite" aria-atomic="true">
        {{ updating ? 'Updating posts…' : `${page.total} ${page.total === 1 ? 'post' : 'posts'}` }}
        <template v-if="!updating && uniformLanguage">· {{ languageName(uniformLanguage) }}</template>
      </span>
      <Button
        v-for="selected in selectedMore"
        :key="selected.key"
        variant="outline"
        size="sm"
        class="min-h-7 rounded-full px-2.5 text-xs"
        :aria-label="`Remove ${selected.label}: ${selected.value} filter`"
        @click="setFilter(selected.key, null)"
      >
        {{ selected.label }}: {{ selected.value }}
        <Icon :icon="X" :size="12" />
      </Button>
      <Button v-if="browsing && !updatesAvailable && !refreshFailed" variant="ghost" size="sm" @click="retry">
        Refresh posts
      </Button>
    </div>
    <div v-if="refreshFailed" class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground" role="status">
      <span>Could not update posts. Showing the previous results.</span>
      <Button variant="outline" size="sm" :is-disabled="updating" @click="retry">Retry</Button>
    </div>
    <div
      v-else-if="updatesAvailable"
      class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground"
      role="status"
    >
      <span>New or updated posts available.</span>
      <Button variant="outline" size="sm" :is-disabled="updating" @click="retry">Refresh posts</Button>
    </div>
    <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Loading posts…</p>
    <div v-else-if="state === 'unavailable'" class="flex items-center gap-3 text-sm text-muted-foreground">
      <span role="status">Could not load this source's posts.</span>
      <Button variant="outline" size="sm" @click="retry">Retry</Button>
    </div>
    <p
      v-else-if="!posts.length"
      class="rounded-2xl border border-dashed border-border p-4 text-sm text-muted-foreground"
    >
      {{ search.trim() || !all ? 'No post matches.' : 'No posts yet. They appear after the first harvest.' }}
    </p>
    <div v-else class="relative" :aria-busy="updating || appending">
      <div
        v-if="props.view === 'rows'"
        class="overflow-hidden rounded-2xl border border-border bg-card"
        role="table"
        aria-label="Posts"
        :aria-rowcount="(page?.total ?? 0) + 1"
      >
        <div
          role="row"
          class="grid grid-cols-[minmax(0,1fr)_7rem_9rem_8rem] gap-4 border-b border-border px-5 py-2.5 text-[13px] font-semibold text-muted-foreground max-md:grid-cols-[minmax(0,1fr)_6rem]"
        >
          <span role="columnheader">Post</span>
          <span role="columnheader">Kind</span>
          <span role="columnheader" class="max-md:hidden">Harvest section</span>
          <span role="columnheader" class="max-md:hidden">Posted</span>
        </div>
        <div ref="post-list" role="rowgroup" :style="padding">
          <div
            v-for="(post, index) in shown"
            :key="post.postId"
            role="row"
            :aria-rowindex="window.start + index + 2"
            v-bind="{ 'data-post-index': window.start + index }"
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
              <ListingCardFacts v-if="post.listing" :listing="post.listing" />
              <span v-else-if="startOf(post)" class="block text-[13px] text-muted-foreground">{{ startOf(post) }}</span>
              <HarvestPill v-if="post.state === 'gone'" tone="outline" class="mt-1">
                <PillIcon name="unlink" />
                removed
              </HarvestPill>
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
      </div>
      <ul
        v-else
        ref="post-list"
        class="grid grid-cols-[repeat(auto-fill,minmax(min(100%,17rem),1fr))] gap-3"
        aria-label="Posts"
        :style="padding"
      >
        <li
          v-for="(post, index) in shown"
          :key="post.postId"
          :aria-posinset="window.start + index + 1"
          :aria-setsize="page?.total"
          v-bind="{ 'data-post-index': window.start + index }"
          class="group/post-card relative isolate flex aspect-[4/5] min-w-0 flex-col justify-end overflow-hidden rounded-2xl border border-border bg-muted hover:border-primary/40 has-[a[data-post-link]:focus-visible]:outline-2 has-[a[data-post-link]:focus-visible]:outline-offset-2 has-[a[data-post-link]:focus-visible]:outline-ring"
        >
          <div class="z-10 flex min-w-0 flex-col gap-2 p-4 text-white">
            <ConsoleLink
              :to="placeOf(post)"
              class="line-clamp-2 text-start text-[15px] leading-snug font-semibold outline-none after:absolute after:inset-0 after:content-['']"
              v-bind="{ 'data-post-link': '' }"
            >
              {{ post.title ?? 'Untitled post' }}
            </ConsoleLink>
            <PostPills
              v-if="!post.listing || post.state === 'gone'"
              :kind="post.kind"
              :pipeline-id="post.pipelineId"
              :publisher-category="post.publisherCategory ?? (hasPublisherCategories ? null : undefined)"
              :language="post.language"
              :removed="post.state === 'gone'"
              :hidden="statedLabels"
              class="-order-1"
              on-image
              compact
            />
            <ListingCardFacts v-if="post.listing" :listing="post.listing" on-image />
            <p
              v-if="post.postedAt || (!post.listing && eventChipOf(post))"
              class="flex min-w-0 flex-wrap items-start gap-x-3 text-[13px] leading-5 text-white/75"
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
                <span v-if="eventChipOf(post)?.timing">· {{ eventChipOf(post)?.timing }}</span>
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
                <CarouselSlide v-for="src in imagesOf(post)" :key="src" class="relative">
                  <img
                    v-if="!failedImages.has(src)"
                    :src="src"
                    alt=""
                    class="size-full object-cover"
                    :class="post.state === 'gone' ? 'grayscale' : ''"
                    loading="lazy"
                    decoding="async"
                    referrerpolicy="no-referrer"
                    v-bind="{ 'data-post-image': '' }"
                    @error="failedImages.add(src)"
                  />
                  <div v-else :class="FallbackTileClass">
                    <Icon
                      :icon="ImageOff"
                      :size="28"
                      class="text-muted-foreground"
                      v-bind="{ 'aria-label': 'Image unavailable' }"
                    />
                  </div>
                </CarouselSlide>
              </CarouselSlides>
            </CarouselViewport>
            <template v-if="imagesOf(post).length > 1">
              <CarouselPrev :class="ArrowClass" class="left-2" />
              <CarouselNext :class="ArrowClass" class="right-2" />
            </template>
          </Carousel>
          <div v-else :class="FallbackTileClass" class="-z-10" aria-hidden="true">
            <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="lg" bare />
          </div>
          <div
            v-if="imagesOf(post).length > 1"
            class="pointer-events-none absolute inset-x-0 top-3 z-10 flex h-8 items-center justify-center opacity-0 transition-opacity group-hover/post-card:opacity-100 group-focus-within/post-card:opacity-100"
          >
            <DotsIndicator
              :count="imagesOf(post).length"
              :index="imageIndexOf(post)"
              :visible-count="5"
              class="rounded-full bg-black/30 px-2 py-1.5 text-white backdrop-blur-md"
            />
          </div>
          <Tooltip
            v-if="webUrlOf(post.url) && post.state !== 'gone'"
            :content="
              post.state === 'suspect'
                ? 'Open the original — the last check could not confirm it is still there'
                : 'Open the original'
            "
            :open-delay="200"
          >
            <a
              :href="webUrlOf(post.url)"
              target="_blank"
              rel="noopener noreferrer"
              class="absolute top-3 right-3 z-20 inline-flex size-8 items-center justify-center rounded-full border border-white/20 bg-black/35 text-white backdrop-blur-md transition-colors hover:bg-black/55 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
              :aria-label="`Open the original of ${post.title ?? 'this post'}`"
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
          </Tooltip>
          <Tooltip v-else :content="unavailableWords(post)" :open-delay="200">
            <span
              class="absolute top-3 right-3 z-20 inline-flex size-8 items-center justify-center rounded-full border border-dashed border-white/30 bg-black/25 text-white/60 backdrop-blur-md"
              role="img"
              tabindex="0"
              :aria-label="unavailableWords(post)"
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
          </Tooltip>
          <div
            class="pointer-events-none absolute inset-x-0 bottom-0 h-3/5 bg-gradient-to-t from-black/80 via-black/45 to-transparent backdrop-blur-xl [-webkit-mask-image:linear-gradient(to_top,black_50%,transparent)] [mask-image:linear-gradient(to_top,black_50%,transparent)]"
            aria-hidden="true"
          ></div>
        </li>
      </ul>
      <div
        v-if="page?.nextCursor"
        class="flex justify-center"
        :class="
          browsing
            ? 'pt-4'
            : 'pointer-events-none absolute inset-x-0 bottom-0 z-20 rounded-b-2xl bg-gradient-to-t from-background/95 to-transparent px-4 pt-12 pb-3'
        "
      >
        <Button
          variant="outline"
          class="pointer-events-auto rounded-full bg-card shadow-md"
          :is-disabled="appending"
          @click="showMore"
        >
          {{ appending ? 'Loading posts…' : appendFailed ? 'Retry loading posts' : 'Show more' }}
        </Button>
      </div>
      <span v-if="browsing" class="sr-only" role="status">
        {{ appending ? 'Loading more posts' : `${posts.length} of ${page?.total ?? posts.length} posts loaded` }}
      </span>
    </div>
  </div>
</template>
