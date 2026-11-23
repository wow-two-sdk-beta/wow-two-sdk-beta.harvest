<script setup lang="ts">
import { computed, ref } from 'vue';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel, SourceProcessedPostModel } from '../../contracts';
import type { ConsolePlace } from './HarvestConsole';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PhotoGallery from './PhotoGallery.vue';
import PillIcon from './PillIcon.vue';
import PostAgePill from './PostAgePill.vue';
import { postCardAspectClass } from './PostGalleryStyles';
import PostLanguagePill from './PostLanguagePill.vue';
import ProviderMark from './ProviderMark.vue';
import SourceIcon from './SourceIcon.vue';
import SourceImage from './SourceImage.vue';
import { kindGlyphOf } from './PostGlyphs';
import { cardModel, cardProvider, entityTypeWords, provenanceLines } from './ProcessedWords';
import { languageName } from './SourceSections';
import { imageSrcOf, webUrlOf } from './SourceWords';

/**
 * The Processed gallery uses the Posts card ratio for the image or source tile and links from the processed title.
 * The detail page keeps the photo gallery in a bounded frame above the title.
 */
defineOptions({ name: 'ProcessedPostHeader' });
const props = withDefaults(
  defineProps<{
    source: Pick<SourceOverviewModel, 'sourceId' | 'family' | 'faviconUrl'>;
    post: SourceProcessedPostModel;
    /** The source's images in capture order, including private copies when available. */
    imageUrls?: readonly string[];
    /** Where the title links, stretched over the card; omitted, the title is the page heading. */
    to?: ConsolePlace;
  }>(),
  { imageUrls: undefined, to: undefined },
);

const tags = computed(() => props.post.tags);
const extraction = computed(() => props.post.extraction);
const failedImage = ref(false);
const image = computed(() => (failedImage.value ? undefined : imageSrcOf(props.post.imageUrl)));
const photos = computed(() => {
  const urls = props.imageUrls?.map(imageSrcOf).filter((url): url is string => !!url);
  const first = imageSrcOf(props.post.imageUrl);
  return urls?.length ? urls : first ? [first] : [];
});
const lines = computed(() => provenanceLines(tags.value, extraction.value));
const titleEn = computed(() =>
  tags.value.titleEn && tags.value.titleEn.trim().toLowerCase() !== props.post.title.trim().toLowerCase()
    ? tags.value.titleEn
    : undefined,
);
const processedTitle = computed(
  () =>
    tags.value.about?.trim() ||
    tags.value.titleEn?.trim() ||
    extraction.value?.event?.name?.trim() ||
    'Untitled processed post',
);
const postLanguage = computed(() => (props.post.language !== 'und' ? props.post.language : undefined));
const entityHelp = computed(() => {
  if (tags.value.status !== 'done')
    return [`Tagging ${tags.value.status}`, tags.value.error].filter(Boolean).join(' · ');
  return tags.value.entityType === 'none' ? 'The tagging pass found no event or listing in this post.' : undefined;
});
const entityLabel = computed(() =>
  props.to && tags.value.status !== 'done' ? `Tagging ${tags.value.status}` : entityTypeWords(tags.value.entityType),
);
const removed = computed(() => props.post.state === 'gone');
const original = computed(() => (removed.value ? undefined : webUrlOf(props.post.url)));
const cover = computed(() => !!props.to && !!image.value);
const tone = computed(() => (cover.value ? 'glass' : 'outline'));
</script>

<template>
  <header
    class="relative isolate flex min-w-0 flex-col gap-3 p-4"
    :class="[
      props.to ? [postCardAspectClass, 'shrink-0 justify-between'] : 'min-h-32',
      cover
        ? 'bg-muted text-white'
        : !props.to && photos.length
          ? 'bg-card'
          : 'bg-gradient-to-br from-primary-soft to-muted',
    ]"
  >
    <ul
      class="z-10 flex min-w-0 flex-wrap items-center gap-1.5 pe-10"
      :aria-label="props.to ? 'Processed post badges' : 'Entity type and AI provider'"
    >
      <li class="min-w-0">
        <Tooltip :content="entityHelp" :is-disabled="!entityHelp" :open-delay="150">
          <HarvestPill :tone="tone" class="max-w-full" v-bind="{ tabindex: entityHelp ? 0 : undefined }">
            <PillIcon :name="kindGlyphOf(tags.entityType ?? 'other')" class="shrink-0" />
            <span class="truncate">{{ entityLabel.toLowerCase() }}</span>
          </HarvestPill>
        </Tooltip>
      </li>
      <li v-if="props.to && postLanguage" class="shrink-0">
        <PostLanguagePill :language="postLanguage" :tone="tone" compact />
      </li>
      <li class="shrink-0">
        <ProviderMark
          :provider="cardProvider(tags, extraction)"
          :model="cardModel(tags, extraction)"
          :lines="lines"
          :on-image="cover"
          pill
          placement="bottom-start"
        />
      </li>
    </ul>
    <SourceImage
      v-if="props.to && image"
      :src="image"
      preset="s3"
      alt=""
      class="absolute inset-0 -z-20 size-full object-cover object-center"
      :class="{ grayscale: removed }"
      loading="lazy"
      decoding="async"
      referrerpolicy="no-referrer"
      v-bind="{ 'data-post-image': '' }"
      @error="failedImage = true"
    />
    <PhotoGallery v-else-if="!props.to && photos.length" :images="photos" :is-muted="removed" fixed-height />
    <div v-else class="absolute end-4 top-1/2 -z-10 -translate-y-1/2 opacity-35" aria-hidden="true">
      <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="lg" bare decorative />
    </div>
    <div class="relative isolate grid min-w-0" :class="props.to ? 'gap-2' : 'gap-0.5'">
      <div
        v-if="cover"
        class="pointer-events-none absolute -inset-x-4 -top-8 -bottom-4 -z-10 bg-gradient-to-t from-black/80 via-black/45 to-transparent backdrop-blur-xl [-webkit-mask-image:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)] [mask-image:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)]"
        aria-hidden="true"
      ></div>
      <ConsoleLink
        v-if="props.to"
        :to="props.to"
        class="line-clamp-2 text-start text-[15px] leading-snug font-semibold outline-none after:absolute after:-inset-[100rem] after:content-['']"
        v-bind="{ 'data-processed-link': '' }"
      >
        {{ processedTitle }}
      </ConsoleLink>
      <h1 v-else class="text-xl leading-snug font-semibold tracking-tight md:text-2xl">{{ props.post.title }}</h1>
      <p v-if="!props.to && titleEn" class="text-[13px] leading-5 text-muted-foreground">{{ titleEn }}</p>
      <div
        class="flex flex-wrap items-center gap-1.5 text-[13px] leading-5 text-muted-foreground"
        v-bind="{ 'data-post-metadata': '' }"
      >
        <PostAgePill v-if="props.post.postedAt" :at="props.post.postedAt" kind="posted" :tone="tone" />
        <PostAgePill :at="props.post.firstSeenAt" kind="harvested" :tone="tone" />
        <span
          v-if="!props.to && postLanguage"
          class="inline-flex items-center gap-1"
          v-bind="{ 'data-post-language': '' }"
        >
          <PillIcon name="language" />
          Post language · {{ languageName(postLanguage) }}
        </span>
        <HarvestPill v-if="removed" :tone="tone" v-bind="{ 'data-post-removed': '' }">
          <PillIcon name="unlink" />
          removed
        </HarvestPill>
      </div>
    </div>
    <Tooltip
      v-if="original"
      :content="
        props.post.state === 'suspect'
          ? 'Open the original — the last check could not confirm it is still there'
          : 'Open the original'
      "
      :open-delay="200"
    >
      <a
        :href="original"
        target="_blank"
        rel="noopener noreferrer"
        class="absolute top-3 right-3 z-20 inline-flex size-8 items-center justify-center rounded-full border border-white/20 bg-black/35 text-white backdrop-blur-md transition-colors hover:bg-black/55 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white"
        :aria-label="props.to ? 'Open the original post' : `Open the original of ${props.post.title ?? 'this post'}`"
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
  </header>
</template>
