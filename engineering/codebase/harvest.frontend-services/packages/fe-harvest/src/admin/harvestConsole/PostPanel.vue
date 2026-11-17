<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PhotoGallery from './PhotoGallery.vue';
import PostPills from './PostPills.vue';
import { useHarvestConsole, type PostDetails } from './HarvestConsole';
import type { SourcePostModel } from '../../contracts';
import { ageOf, changedWords, dateTimeOf, imageSrcOf, webUrlOf, whenOf } from './SourceWords';

/**
 * The captured post, with shared provenance and the evidence sections selected by its source registry.
 */
defineOptions({ name: 'PostPanel' });
const props = defineProps<{ sourceId: string; postId: string }>();
const session = useHarvestConsole();
const source = computed(() => session.overviewOf(props.sourceId));
const view = computed(() => session.capabilities.value[props.sourceId]?.configuration.postView);
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
      session.querySourcePosts(props.sourceId, { postId, limit: 1 }, controller.signal),
    ]);
    if (controller.signal.aborted) return;
    state.value = result.ok ? 'ready' : 'unavailable';
    if (result.ok) details.value = result.value;
    if (list.ok) listed.value = list.value.items[0];
  },
  { immediate: true },
);

const post = computed(() => details.value?.post);
const base = computed(() => post.value?.base);
const sourceLink = computed(() => {
  const links = post.value?.links.filter((link) => link.sourceId === props.sourceId) ?? [];
  return links.find((link) => link.role === 'primary') ?? links[0];
});
const gone = computed(() => (sourceLink.value?.state ?? listed.value?.state ?? post.value?.state) === 'gone');
const original = computed(() => webUrlOf(sourceLink.value?.url ?? listed.value?.url));
/** Why the original cannot be opened, when it cannot. */
const unavailable = computed(() => (gone.value ? 'Removed from the source' : 'No link to the original'));
const timestamps = computed(() => {
  const link = sourceLink.value;
  return [
    { label: 'Published', value: listed.value?.postedAt ?? base.value?.postedAt?.value },
    { label: 'First seen', value: listed.value?.firstSeenAt ?? post.value?.firstSeenAtUtc },
    { label: 'Last captured', value: link?.capturedAtUtc },
    { label: 'Last checked', value: link?.lastCheckedAtUtc },
  ].filter((timestamp): timestamp is { label: string; value: string } => !!timestamp.value);
});
/** The source's image list prefers private copies, including when the post's captured URL is older. */
const images = computed(() => {
  const originals = (base.value?.media ?? []).filter((item) => item.kind === 'image').map((item) => item.url);
  const stored = listed.value?.imageUrls ?? [];
  const urls = originals.length
    ? originals.map((url, index) => {
        const copy = stored[index];
        return copy?.startsWith(`/api/sources/${props.sourceId}/assets/`) ? copy : url;
      })
    : stored;
  return urls.map(imageSrcOf).filter((url): url is string => !!url);
});
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
          <PostPills
            :kind="kind"
            :pipeline-id="listed?.pipelineId"
            :language="listed?.language"
            :source-name="source?.name"
            :family="source?.family"
            :removed="gone"
          />
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

      <PhotoGallery v-if="view?.showPhotos !== false" :images="images" :is-muted="gone" />

      <div class="grid gap-5 lg:grid-cols-2">
        <section
          v-if="sourceLink?.externalKey || timestamps.length"
          aria-label="Capture"
          class="min-w-0 rounded-2xl border border-border bg-card p-5"
        >
          <h2 class="text-[15px] font-semibold">Capture</h2>
          <dl v-if="sourceLink?.externalKey" class="mt-3 grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 text-[15px]">
            <dt class="text-muted-foreground">Original post ID</dt>
            <dd class="break-words font-semibold">{{ sourceLink.externalKey }}</dd>
          </dl>
          <div v-if="timestamps.length" class="mt-3 flex flex-wrap gap-2">
            <Tooltip
              v-for="timestamp in timestamps"
              :key="timestamp.label"
              :content="dateTimeOf(timestamp.value)"
              :open-delay="200"
            >
              <HarvestPill
                tone="outline"
                size="md"
                v-bind="{ tabindex: 0 }"
                class="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              >
                <span class="text-muted-foreground">{{ timestamp.label }}</span>
                <time :datetime="timestamp.value">{{ ageOf(timestamp.value, undefined, true) }}</time>
              </HarvestPill>
            </Tooltip>
          </div>
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
      <section
        v-if="view?.showSourceText && base?.text"
        aria-label="Captured text"
        class="rounded-2xl border border-border bg-card p-5"
      >
        <h2 class="text-[15px] font-semibold">Captured text</h2>
        <p class="mt-3 whitespace-pre-wrap break-words text-sm">{{ base.text }}</p>
      </section>
    </template>
  </div>
</template>
