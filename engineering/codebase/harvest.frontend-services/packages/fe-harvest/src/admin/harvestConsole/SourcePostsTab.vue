<script setup lang="ts">
import { computed, onScopeDispose, ref, shallowRef, watch } from 'vue';
import { SearchInput } from '@wow-two-beta/fe/presentation/forms';
import type { SourceOverviewModel, SourcePostModel } from '../../contracts';
import FilterChip from './FilterChip.vue';
import HarvestPill from './HarvestPill.vue';
import PostModal from './PostModal.vue';
import SourceIcon from './SourceIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import { agoOf, dateOf, imageSrcOf, kindPlural, timingOf, whenOf, type GalleryView } from './SourceWords';

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
const openId = ref<string | null>(null);
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
const opened = computed(() => posts.value.find((post) => post.postId === openId.value));

/** A post's own date when it has one, with `ongoing` or `finished` once its stated end allows — `1 Oct 2026 · ongoing`. */
function startOf(post: SourcePostModel) {
  return (
    [post.startsAt ? dateOf(post.startsAt) : undefined, timingOf(post.startsAt, post.endsAt)]
      .filter(Boolean)
      .join(' · ') || undefined
  );
}
/** The line under a card's title: its date if any, and when SCFD first saw it — `21 Nov 2026 · seen 3 days ago`. */
function cardLine(post: SourcePostModel) {
  return [startOf(post), `seen ${agoOf(post.firstSeenAt)}`].filter(Boolean).join(' · ');
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
        <span role="columnheader" class="max-md:hidden">Harvested</span>
      </div>
      <div
        v-for="post in shown"
        :key="post.postId"
        role="row"
        class="relative grid min-h-16 grid-cols-[minmax(0,1fr)_7rem_9rem_8rem] items-center gap-4 border-b border-border px-5 py-2.5 last:border-b-0 hover:bg-muted/40 has-[button:focus-visible]:outline-2 has-[button:focus-visible]:-outline-offset-2 has-[button:focus-visible]:outline-ring max-md:grid-cols-[minmax(0,1fr)_6rem]"
      >
        <span role="cell" class="min-w-0">
          <button
            type="button"
            class="block max-w-full truncate text-start text-[15px] font-semibold outline-none after:absolute after:inset-0 after:content-['']"
            @click="openId = post.postId"
          >
            {{ post.title ?? 'Untitled post' }}
          </button>
          <span v-if="startOf(post)" class="block text-[13px] text-muted-foreground">{{ startOf(post) }}</span>
        </span>
        <span role="cell">
          <HarvestPill :tone="post.kind === 'event' ? 'success' : 'outline'">{{ post.kind }}</HarvestPill>
        </span>
        <span role="cell" class="truncate max-md:hidden">{{ sectionOf(post) }}</span>
        <span role="cell" class="max-md:hidden">{{ whenOf(post.firstSeenAt) }}</span>
      </div>
    </div>
    <ul v-else class="grid grid-cols-[repeat(auto-fill,minmax(16rem,1fr))] gap-3" aria-label="Posts">
      <li
        v-for="post in shown"
        :key="post.postId"
        class="group relative isolate flex aspect-[4/5] min-w-0 flex-col justify-end overflow-hidden rounded-2xl border border-border bg-muted hover:border-primary/40 has-[button:focus-visible]:outline-2 has-[button:focus-visible]:outline-offset-2 has-[button:focus-visible]:outline-ring"
      >
        <img
          v-if="imageSrcOf(post.imageUrl)"
          :src="imageSrcOf(post.imageUrl)"
          alt=""
          class="absolute inset-0 -z-10 size-full object-cover transition-transform duration-300 group-hover:scale-105 motion-reduce:transition-none motion-reduce:group-hover:scale-100"
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
        <div
          class="pointer-events-none absolute inset-x-0 bottom-0 h-3/5 bg-gradient-to-t from-black/80 via-black/45 to-transparent backdrop-blur-xl [-webkit-mask-image:linear-gradient(to_top,black_50%,transparent)] [mask-image:linear-gradient(to_top,black_50%,transparent)]"
          aria-hidden="true"
        ></div>
        <div class="relative flex h-[8.5rem] min-w-0 flex-col gap-2 p-4 text-white">
          <div class="flex flex-wrap gap-1.5">
            <HarvestPill :tone="post.kind === 'event' ? 'glassSuccess' : 'glass'">{{ post.kind }}</HarvestPill>
            <HarvestPill v-if="post.pipelineId" tone="glass">{{ sectionOf(post) }}</HarvestPill>
          </div>
          <button
            type="button"
            class="line-clamp-2 min-h-[2lh] text-start text-[15px] leading-snug font-semibold outline-none after:absolute after:inset-0 after:content-['']"
            @click="openId = post.postId"
          >
            {{ post.title ?? 'Untitled post' }}
          </button>
          <p class="mt-auto truncate text-[13px] text-white/75">{{ cardLine(post) }}</p>
        </div>
      </li>
    </ul>

    <PostModal
      :open="openId !== null"
      :post="opened"
      :source-name="props.source.name"
      :section-name="opened ? sectionOf(opened) : undefined"
      @update:open="(open) => !open && (openId = null)"
    />
  </div>
</template>
