<script setup lang="ts">
import { computed, shallowRef, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import {
  Modal,
  ModalBody,
  ModalClose,
  ModalContent,
  ModalHeader,
  ModalTitle,
} from '@wow-two-beta/fe/presentation/overlays';
import type { SourcePostModel } from '../../contracts';
import HarvestPill from './HarvestPill.vue';
import { useHarvestConsole, type PostDetails } from './HarvestConsole';
import { changedWords, dateOf, dateTimeOf, imageSrcOf, whenOf } from './SourceWords';

/**
 * One post over the page, with no address of its own: its kind, section and source, its images — one large, the rest
 * beside it, drawn here and never linked out — its facts — when it starts,
 * where, the offers, when it was posted and harvested — and its revisions in words, then a link to the original.
 * SCFD is not a place to read posts, so nothing here routes.
 */
defineOptions({ name: 'PostModal' });
const props = defineProps<{
  open: boolean;
  /** The post as the list shows it; the modal reads its facts and revisions when it opens. */
  post?: SourcePostModel;
  sourceName: string;
  sectionName?: string;
}>();
const emit = defineEmits<{ 'update:open': [open: boolean] }>();
const session = useHarvestConsole();
const details = shallowRef<PostDetails>();
const state = shallowRef<'loading' | 'ready' | 'unavailable'>('loading');
let read: AbortController | null = null;

watch(
  () => [props.open, props.post?.postId] as const,
  async ([open, postId]) => {
    read?.abort();
    if (!open || !postId) return;
    const controller = new AbortController();
    read = controller;
    state.value = 'loading';
    details.value = undefined;
    const result = await session.readPost(postId, controller.signal);
    if (controller.signal.aborted) return;
    state.value = result.ok ? 'ready' : 'unavailable';
    if (result.ok) details.value = result.value;
  },
  { immediate: true },
);

/** Allows only a public web address. @internal */
function webUrl(value?: string) {
  if (!value) return undefined;
  try {
    const url = new URL(value);
    return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password ? url.href : undefined;
  } catch {
    return undefined;
  }
}

const base = computed(() => details.value?.post.base);
const facts = computed(() => {
  const post = details.value?.post;
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
      value: start ? dateTimeOf(start.value) : props.post?.startsAt ? dateTimeOf(props.post.startsAt) : undefined,
    },
    { label: 'Place', value: place?.text },
    { label: 'Address', value: address?.text },
    { label: 'Offers', value: value?.prices?.length ? String(value.prices.length) : undefined },
    { label: 'Posted', value: value?.postedAt ? dateOf(value.postedAt.value) : undefined },
    {
      label: 'Harvested',
      value: post ? whenOf(post.firstSeenAtUtc) : props.post ? whenOf(props.post.firstSeenAt) : undefined,
    },
  ].filter((item): item is { label: string; value: string } => !!item.value);
});
/** The post's images, as harvested: each `media` item of kind `image`, by its URL. */
const images = computed(() =>
  (base.value?.media ?? [])
    .filter((item) => item.kind === 'image')
    .map((item) => imageSrcOf(item.url))
    .filter((url): url is string => !!url),
);
const original = computed(() => webUrl(props.post?.url ?? details.value?.post.links[0]?.url));
const kind = computed(() => props.post?.kind ?? base.value?.kind ?? 'post');
</script>

<template>
  <Modal :open="props.open" @update:open="emit('update:open', $event)">
    <ModalContent size="lg" v-bind="{ 'aria-label': 'Post' }">
      <ModalHeader>
        <ModalTitle>{{ props.post?.title ?? base?.title ?? 'Untitled post' }}</ModalTitle>
        <ModalClose />
      </ModalHeader>
      <ModalBody class="space-y-5" v-bind="{ 'data-post-modal': '' }">
        <div class="flex flex-wrap gap-2">
          <HarvestPill :tone="kind === 'event' ? 'success' : 'outline'">{{ kind }}</HarvestPill>
          <HarvestPill v-if="props.sectionName" tone="outline">{{ props.sectionName }}</HarvestPill>
          <HarvestPill tone="outline">{{ props.sourceName }}</HarvestPill>
        </div>
        <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Reading the post…</p>
        <p v-else-if="state === 'unavailable'" class="text-sm text-muted-foreground" role="status">
          Could not read this post. Close it and try again.
        </p>
        <template v-else>
          <div
            v-if="images.length"
            class="grid gap-2.5"
            :class="images.length > 1 ? 'grid-cols-[minmax(0,3fr)_minmax(0,1fr)]' : ''"
            v-bind="{ 'data-post-images': images.length }"
          >
            <img
              :src="images[0]"
              :alt="`Image 1 of ${images.length} of this post`"
              class="h-64 w-full rounded-xl bg-muted object-cover"
              loading="lazy"
              decoding="async"
              referrerpolicy="no-referrer"
            />
            <div v-if="images.length > 1" class="flex max-h-64 flex-col gap-2.5 overflow-y-auto">
              <img
                v-for="(src, index) in images.slice(1)"
                :key="index"
                :src="src"
                :alt="`Image ${index + 2} of ${images.length} of this post`"
                class="h-[7.4rem] w-full shrink-0 rounded-xl bg-muted object-cover"
                loading="lazy"
                decoding="async"
                referrerpolicy="no-referrer"
              />
            </div>
          </div>
          <dl v-if="facts.length" class="grid grid-cols-[8rem_minmax(0,1fr)] gap-x-4 gap-y-2.5 text-[15px]">
            <template v-for="fact in facts" :key="fact.label">
              <dt class="text-muted-foreground">{{ fact.label }}</dt>
              <dd class="font-semibold">{{ fact.value }}</dd>
            </template>
          </dl>
          <section aria-label="Revisions">
            <h3 class="text-[15px] font-semibold">Revisions</h3>
            <ul v-if="details?.revisions.length" class="mt-2 divide-y divide-border border-b border-border">
              <li
                v-for="revision in details.revisions"
                :key="revision.revision"
                class="grid grid-cols-[8rem_9rem_minmax(0,1fr)] gap-3 py-2.5 text-[15px]"
              >
                <span class="font-semibold">Revision {{ revision.revision }}</span>
                <span>{{ whenOf(revision.at) }}</span>
                <span>{{ changedWords(revision.changed) }}</span>
              </li>
            </ul>
            <p v-else class="mt-2 text-sm text-muted-foreground">
              Revision {{ details?.post.revision ?? props.post?.revision }} · harvested before revisions were kept.
            </p>
          </section>
        </template>
        <div v-if="original" class="flex flex-wrap gap-2">
          <Button as-child variant="outline" size="sm">
            <a :href="original" target="_blank" rel="noopener noreferrer">Open the original</a>
          </Button>
        </div>
      </ModalBody>
    </ModalContent>
  </Modal>
</template>
