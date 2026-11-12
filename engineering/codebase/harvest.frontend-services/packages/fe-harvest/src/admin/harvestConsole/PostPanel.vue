<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import { useHarvestConsole, type PostDetails } from './HarvestConsole';
import type { SourcePostModel } from '../../contracts';
import { changedWords, dateOf, dateTimeOf, imageSrcOf, timingOf, webUrlOf, whenOf } from './SourceWords';

/**
 * One post on its own page, `/sources/{id}/posts/{postId}`: its kind and source, its images — one large, the rest
 * beside it, drawn here and never linked out — its facts — when it starts, where, the offers, when it was posted and
 * harvested — and its revisions in words. "Open the original" sits at the top right; once the post is gone from its
 * source, or has no link, the button is shown disabled and says why.
 */
defineOptions({ name: 'PostPanel' });
const props = defineProps<{ sourceId: string; postId: string }>();
const session = useHarvestConsole();
const source = computed(() => session.overviewOf(props.sourceId));
const details = shallowRef<PostDetails>();
/** The post as its source's list shows it: the section, the stated start and end, and the link. */
const listed = shallowRef<SourcePostModel>();
const state = shallowRef<'loading' | 'ready' | 'unavailable'>('loading');
let read: AbortController | null = null;

watch(
  () => props.postId,
  async (postId) => {
    read?.abort();
    const controller = new AbortController();
    read = controller;
    state.value = 'loading';
    details.value = undefined;
    listed.value = undefined;
    const [result, list] = await Promise.all([
      session.readPost(postId, controller.signal),
      session.readSourcePosts(props.sourceId, controller.signal),
    ]);
    if (controller.signal.aborted) return;
    state.value = result.ok ? 'ready' : 'unavailable';
    if (result.ok) details.value = result.value;
    if (list.ok) listed.value = list.value.find((item) => item.postId === postId);
  },
  { immediate: true },
);

const post = computed(() => details.value?.post);
const base = computed(() => post.value?.base);
const gone = computed(() => (post.value?.state ?? listed.value?.state) === 'gone');
const original = computed(() =>
  webUrlOf(
    post.value?.links.find((link) => link.role === 'primary')?.url ?? post.value?.links[0]?.url ?? listed.value?.url,
  ),
);
/** Why the original cannot be opened, when it cannot. */
const unavailable = computed(() => (gone.value ? 'Removed from the source' : 'No link to the original'));
const facts = computed(() => {
  const value = base.value;
  const start = value?.dates?.find((item) => /start|begin|event/i.test(item.role)) ?? value?.dates?.[0];
  const mentions = value?.location?.mentions ?? [];
  const address = mentions.find((item) => /address|street/i.test(item.kind));
  const place =
    mentions.find((item) => item !== address && /venue|place|poi|city/i.test(item.kind)) ??
    mentions.find((item) => item !== address);
  return [
    {
      label: 'Starts',
      value: [
        start ? dateTimeOf(start.value) : listed.value?.startsAt ? dateTimeOf(listed.value.startsAt) : undefined,
        timingOf(start?.value ?? listed.value?.startsAt, listed.value?.endsAt),
      ]
        .filter(Boolean)
        .join(' · '),
    },
    { label: 'Place', value: place?.text },
    { label: 'Address', value: address?.text },
    { label: 'Offers', value: value?.prices?.length ? String(value.prices.length) : undefined },
    { label: 'Posted', value: value?.postedAt ? dateOf(value.postedAt.value) : undefined },
    { label: 'Harvested', value: post.value ? whenOf(post.value.firstSeenAtUtc) : undefined },
  ].filter((item): item is { label: string; value: string } => !!item.value);
});
/** The post's images, as harvested: each `media` item of kind `image`, by its URL. */
const images = computed(() =>
  (base.value?.media ?? [])
    .filter((item) => item.kind === 'image')
    .map((item) => imageSrcOf(item.url))
    .filter((url): url is string => !!url),
);
const kind = computed(() => base.value?.kind ?? 'post');
</script>

<template>
  <div class="flex min-w-0 flex-col gap-5" role="region" aria-label="Post">
    <ConsoleLink
      :to="{ kind: 'source', id: props.sourceId, tab: 'posts' }"
      class="inline-flex w-fit items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
    >
      <svg
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2.2"
        aria-hidden="true"
      >
        <path d="M15 6l-6 6 6 6" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
      Posts of {{ source?.name ?? 'the source' }}
    </ConsoleLink>

    <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Reading the post…</p>
    <div v-else-if="state === 'unavailable'" class="rounded-2xl border border-border bg-card p-2">
      <EmptyState title="Could not read this post" description="It may be gone, or the console could not reach SCFD." />
    </div>
    <template v-else>
      <header class="flex flex-wrap items-start justify-between gap-4">
        <div class="flex min-w-0 flex-1 flex-col gap-3">
          <h1 class="text-2xl font-semibold tracking-tight">{{ base?.title ?? 'Untitled post' }}</h1>
          <div class="flex flex-wrap gap-2">
            <HarvestPill :tone="kind === 'event' ? 'success' : 'outline'">{{ kind }}</HarvestPill>
            <HarvestPill v-if="source" tone="outline">{{ source.name.toLowerCase() }}</HarvestPill>
            <HarvestPill v-if="listed?.pipelineId" tone="outline">
              {{ session.sectionLabel(listed.pipelineId).toLowerCase() }}
            </HarvestPill>
            <HarvestPill v-if="gone" tone="danger">removed</HarvestPill>
          </div>
        </div>
        <Button v-if="original && !gone" as-child variant="outline">
          <a :href="original" target="_blank" rel="noopener noreferrer">
            Open the original
            <svg
              width="14"
              height="14"
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
        </Button>
        <Button v-else variant="outline" is-disabled>
          <svg
            width="14"
            height="14"
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
          {{ unavailable }}
        </Button>
      </header>
      <p v-if="gone" class="rounded-xl border border-dashed border-border p-3 text-sm text-muted-foreground">
        Removed from the source: its last check found no live link. SCFD keeps the post as it was harvested.
      </p>

      <div
        v-if="images.length"
        class="grid gap-2.5"
        :class="images.length > 1 ? 'grid-cols-[minmax(0,3fr)_minmax(0,1fr)]' : ''"
        v-bind="{ 'data-post-images': images.length }"
      >
        <img
          :src="images[0]"
          :alt="`Image 1 of ${images.length} of this post`"
          class="h-80 w-full rounded-2xl bg-muted object-cover"
          :class="gone ? 'grayscale' : ''"
          loading="lazy"
          decoding="async"
          referrerpolicy="no-referrer"
        />
        <div v-if="images.length > 1" class="flex max-h-80 flex-col gap-2.5 overflow-y-auto">
          <img
            v-for="(src, index) in images.slice(1)"
            :key="index"
            :src="src"
            :alt="`Image ${index + 2} of ${images.length} of this post`"
            class="aspect-[4/3] w-full shrink-0 rounded-xl bg-muted object-cover"
            :class="gone ? 'grayscale' : ''"
            loading="lazy"
            decoding="async"
            referrerpolicy="no-referrer"
          />
        </div>
      </div>

      <div class="grid gap-5 lg:grid-cols-2">
        <section v-if="facts.length" aria-label="Facts" class="rounded-2xl border border-border bg-card p-5">
          <h2 class="text-[15px] font-semibold">Facts</h2>
          <dl class="mt-3 grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 gap-y-2.5 text-[15px]">
            <template v-for="fact in facts" :key="fact.label">
              <dt class="text-muted-foreground">{{ fact.label }}</dt>
              <dd class="font-semibold">{{ fact.value }}</dd>
            </template>
          </dl>
        </section>
        <section aria-label="Revisions" class="rounded-2xl border border-border bg-card p-5">
          <h2 class="text-[15px] font-semibold">Revisions</h2>
          <ul v-if="details?.revisions.length" class="mt-3 divide-y divide-border">
            <li
              v-for="revision in details.revisions"
              :key="revision.revision"
              class="grid grid-cols-[7rem_8rem_minmax(0,1fr)] gap-3 py-2.5 text-[15px]"
            >
              <span class="font-semibold">Revision {{ revision.revision }}</span>
              <span>{{ whenOf(revision.at) }}</span>
              <span>{{ changedWords(revision.changed) }}</span>
            </li>
          </ul>
          <p v-else class="mt-3 text-sm text-muted-foreground">
            Revision {{ post?.revision }} · harvested before revisions were kept.
          </p>
        </section>
      </div>
    </template>
  </div>
</template>
