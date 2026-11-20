<script setup lang="ts">
import { computed, ref } from 'vue';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel, SourceProcessedPostModel } from '../../contracts';
import type { ConsolePlace } from './HarvestConsole';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import ProviderMark from './ProviderMark.vue';
import SourceIcon from './SourceIcon.vue';
import SourceImage from './SourceImage.vue';
import { kindGlyphOf } from './PostGlyphs';
import { cardModel, cardProvider, entityTypeWords, provenanceLines, VerdictWords, verdictTone } from './ProcessedWords';
import { languageName } from './SourceSections';
import { agoOf, imageSrcOf, webUrlOf, type PillTone } from './SourceWords';

/**
 * The media header of a processed post, shared by its card and its page: the post's first image (a tile with the source
 * icon when it has none), the entity type, relevance verdict and language as glass pills with the answering AI's brand
 * mark as the last pill, the ↗ open-the-original button top right, and the title with its English title and when the
 * post was first seen over a blurred fade anchored to that block (the `mediaCard` content-blur rule). With `to` the title
 * is the card's link, stretched over the whole card by its `::after`; without it the title is the page's `h1`. `wide`
 * gives the page's broader ratio.
 */
defineOptions({ name: 'ProcessedPostHeader' });
const props = withDefaults(
  defineProps<{
    source: Pick<SourceOverviewModel, 'sourceId' | 'family' | 'faviconUrl'>;
    post: SourceProcessedPostModel;
    /** Where the title links, stretched over the card; omitted, the title is the page heading. */
    to?: ConsolePlace;
    wide?: boolean;
  }>(),
  { to: undefined, wide: false },
);

const tags = computed(() => props.post.tags);
const extraction = computed(() => props.post.extraction);
const failedImage = ref(false);
const image = computed(() => (failedImage.value ? undefined : imageSrcOf(props.post.imageUrl)));
const lines = computed(() => provenanceLines(tags.value, extraction.value));
const titleEn = computed(() =>
  tags.value.titleEn && tags.value.titleEn.trim().toLowerCase() !== props.post.title.trim().toLowerCase()
    ? tags.value.titleEn
    : undefined,
);
const subline = computed(() =>
  [titleEn.value, `first seen ${agoOf(props.post.firstSeenAt)}`].filter(Boolean).join(' · '),
);
const classification = computed(() => props.post.classification);
const verdictTip = computed(() => {
  const value = classification.value;
  if (!value) return [];
  return [
    value.reason,
    typeof value.confidence === 'number' ? `${Math.round(value.confidence * 100)}% confident` : undefined,
    value.classifier,
  ].filter((line): line is string => !!line);
});
const verdictPillTone = computed<PillTone>(() => {
  const verdict = classification.value?.verdict;
  if (!verdict) return 'neutral';
  if (!image.value) return verdictTone(verdict);
  return verdict === 'relevant' ? 'glassSuccess' : 'glass';
});
const removed = computed(() => props.post.state === 'gone');
const original = computed(() => (removed.value ? undefined : webUrlOf(props.post.url)));
/** The header's fill and ratio: the image at 16:9 (wider on the page), else a tinted tile as tall as its content needs. */
const headerClass = computed(() => {
  if (image.value)
    return ['justify-between bg-muted text-white', props.wide ? 'aspect-[16/9] md:aspect-[21/8]' : 'aspect-[16/9]'];
  return [
    'bg-gradient-to-br from-primary-soft to-muted text-foreground',
    props.wide ? 'min-h-32 justify-between' : 'min-h-20',
  ];
});
</script>

<template>
  <header class="relative isolate flex min-w-0 flex-col gap-3 p-4" :class="headerClass">
    <SourceImage
      v-if="image"
      :src="image"
      alt=""
      class="absolute inset-0 -z-20 size-full object-cover"
      :class="removed ? 'grayscale' : ''"
      loading="lazy"
      decoding="async"
      referrerpolicy="no-referrer"
      v-bind="{ 'data-post-image': '' }"
      @error="failedImage = true"
    />
    <div v-else class="absolute end-4 top-1/2 -z-10 -translate-y-1/2 opacity-35" aria-hidden="true">
      <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="lg" bare decorative />
    </div>
    <ul
      class="z-10 flex min-w-0 flex-wrap items-center gap-1.5 pe-10"
      aria-label="Entity type, verdict, language and AI provider"
    >
      <li>
        <HarvestPill :tone="image ? 'glass' : 'info'">
          <PillIcon :name="kindGlyphOf(tags.entityType ?? 'other')" />
          {{ entityTypeWords(tags.entityType) }}
        </HarvestPill>
      </li>
      <li v-if="classification">
        <Tooltip v-if="verdictTip.length" :open-delay="150">
          <HarvestPill :tone="verdictPillTone" v-bind="{ tabindex: 0 }">
            {{ VerdictWords[classification.verdict] }}
          </HarvestPill>
          <template #content>
            <span class="grid max-w-64 gap-0.5 text-xs leading-4">
              <span v-for="line in verdictTip" :key="line">{{ line }}</span>
            </span>
          </template>
        </Tooltip>
        <HarvestPill v-else :tone="verdictPillTone">{{ VerdictWords[classification.verdict] }}</HarvestPill>
      </li>
      <li v-if="props.post.language">
        <HarvestPill :tone="image ? 'glass' : 'outline'">
          <PillIcon name="language" />
          {{ languageName(props.post.language) }}
        </HarvestPill>
      </li>
      <li v-if="removed">
        <HarvestPill :tone="image ? 'glass' : 'outline'">
          <PillIcon name="unlink" />
          removed
        </HarvestPill>
      </li>
      <li>
        <ProviderMark
          :provider="cardProvider(tags, extraction)"
          :model="cardModel(tags, extraction)"
          :lines="lines"
          :on-image="!!image"
          pill
          placement="bottom-start"
        />
      </li>
    </ul>
    <div class="relative isolate grid min-w-0 gap-0.5">
      <div
        v-if="image"
        class="pointer-events-none absolute -inset-x-4 -top-8 -bottom-4 -z-10 bg-gradient-to-t from-black/80 via-black/45 to-transparent backdrop-blur-xl [-webkit-mask-image:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)] [mask-image:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)]"
        aria-hidden="true"
      ></div>
      <ConsoleLink
        v-if="props.to"
        :to="props.to"
        class="line-clamp-2 text-start text-[15px] leading-snug font-semibold outline-none after:absolute after:-inset-[100rem] after:content-['']"
        v-bind="{ 'data-processed-link': '' }"
      >
        {{ props.post.title }}
      </ConsoleLink>
      <h1 v-else class="text-xl leading-snug font-semibold tracking-tight md:text-2xl">{{ props.post.title }}</h1>
      <p class="line-clamp-2 text-[13px] leading-5" :class="image ? 'text-white/75' : 'text-muted-foreground'">
        {{ subline }}
      </p>
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
        :aria-label="`Open the original of ${props.post.title ?? 'this post'}`"
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
