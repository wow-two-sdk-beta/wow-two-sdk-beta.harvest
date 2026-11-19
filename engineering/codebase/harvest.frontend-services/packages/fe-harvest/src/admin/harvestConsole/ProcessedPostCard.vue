<script setup lang="ts">
import { computed } from 'vue';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceProcessedPostModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import ProviderMark from './ProviderMark.vue';
import { kindGlyphOf } from './PostGlyphs';
import {
  cardProvider,
  entityTypeWords,
  eventLines,
  listingLines,
  provenanceLines,
  unsureFields,
  VerdictWords,
  verdictTone,
} from './ProcessedWords';
import { agoOf } from './SourceWords';

/**
 * One post as the AI passes read it: the entity type, the primary domain and the others as chips, the topics, what it
 * is about, its timeframe and English title, the relevance verdict and, for an extracted event or listing, a compact
 * summary. The provider shows as an icon whose tooltip carries the model, cost, latency and prompt version; a small `?`
 * marks the fields the model was unsure of.
 */
defineOptions({ name: 'ProcessedPostCard' });
const props = defineProps<{ sourceId: string; post: SourceProcessedPostModel }>();

const tags = computed(() => props.post.tags);
const extraction = computed(() => props.post.extraction);
const primary = computed(() => tags.value.domains.find((domain) => domain.primary));
const others = computed(() => tags.value.domains.filter((domain) => !domain.primary));
const unsure = computed(() => unsureFields(tags.value, extraction.value));
const lines = computed(() => provenanceLines(tags.value, extraction.value));
const summary = computed(() => {
  const value = extraction.value;
  if (!value || value.status !== 'done') return [];
  if (value.event) return eventLines(value.event);
  if (value.listing) return listingLines(value.listing);
  return [];
});
const titleEn = computed(() =>
  tags.value.titleEn && tags.value.titleEn.trim().toLowerCase() !== props.post.title.trim().toLowerCase()
    ? tags.value.titleEn
    : undefined,
);
const place = computed(() => ({ kind: 'source', id: props.sourceId, tab: 'posts', post: props.post.postId }) as const);
</script>

<template>
  <article class="grid min-w-0 gap-3 rounded-2xl border border-border bg-card p-4" :aria-label="props.post.title">
    <header class="flex min-w-0 items-start gap-3">
      <div class="grid min-w-0 flex-1 gap-0.5">
        <ConsoleLink :to="place" class="truncate text-[15px] font-semibold hover:underline">
          {{ props.post.title }}
        </ConsoleLink>
        <p class="truncate text-[13px] text-muted-foreground">
          <span v-if="titleEn">{{ titleEn }} ·</span>
          <span>first seen {{ agoOf(props.post.firstSeenAt) }}</span>
          <span v-if="!tags.current">· tags from an earlier text</span>
        </p>
      </div>
      <div class="flex shrink-0 items-center gap-1.5">
        <Tooltip v-if="unsure.length" :content="`Unsure: ${unsure.join(', ')}`" :open-delay="150">
          <span
            class="inline-grid size-5 place-items-center rounded-full border border-warning/40 bg-warning-soft text-[11px] font-bold text-warning-soft-foreground"
            tabindex="0"
            role="img"
            :aria-label="`Unsure: ${unsure.join(', ')}`"
          >
            ?
          </span>
        </Tooltip>
        <HarvestPill
          v-if="props.post.classification"
          :tone="verdictTone(props.post.classification.verdict)"
          v-bind="{ title: props.post.classification.reason }"
        >
          {{ VerdictWords[props.post.classification.verdict] }}
        </HarvestPill>
        <ProviderMark :provider="cardProvider(tags, extraction)" :lines="lines" />
      </div>
    </header>

    <p v-if="tags.status !== 'done'" class="text-sm text-muted-foreground" role="status">
      Tagging {{ tags.status }}
      <template v-if="tags.error">· {{ tags.error }}</template>
    </p>
    <template v-else>
      <ul class="flex min-w-0 flex-wrap items-center gap-1.5" aria-label="Tags">
        <li>
          <HarvestPill tone="info">
            <PillIcon :name="kindGlyphOf(tags.entityType ?? 'other')" />
            {{ entityTypeWords(tags.entityType) }}
          </HarvestPill>
        </li>
        <li v-if="primary">
          <HarvestPill tone="outline" shrinkable v-bind="{ title: primary.key }">{{ primary.label }}</HarvestPill>
        </li>
        <li v-for="domain in others" :key="domain.key">
          <HarvestPill shrinkable v-bind="{ title: domain.key }">{{ domain.label }}</HarvestPill>
        </li>
        <li v-for="topic in tags.topics" :key="topic">
          <HarvestPill tone="dashed" shrinkable>#{{ topic }}</HarvestPill>
        </li>
        <li v-if="tags.timeframe">
          <HarvestPill tone="dashed">
            <PillIcon name="clock" />
            {{ tags.timeframe }}
          </HarvestPill>
        </li>
      </ul>
      <p v-if="tags.about" class="text-sm">{{ tags.about }}</p>
    </template>

    <section
      v-if="extraction"
      class="grid gap-1 rounded-xl bg-muted/50 px-3 py-2 text-[13px]"
      :aria-label="extraction.useCase === 'extract-listing' ? 'Listing extraction' : 'Event extraction'"
    >
      <p class="flex flex-wrap items-center gap-1.5 text-xs font-semibold text-muted-foreground">
        <span>{{ extraction.useCase === 'extract-listing' ? 'Listing' : 'Event' }}</span>
        <span v-if="extraction.role">· {{ extraction.role }}</span>
        <span v-if="extraction.status !== 'done'">
          · {{ extraction.status }}
          <template v-if="extraction.error">({{ extraction.error }})</template>
        </span>
      </p>
      <p v-for="line in summary" :key="line.key" class="min-w-0 truncate" v-bind="{ 'data-line': line.key }">
        {{ line.text }}
      </p>
    </section>
  </article>
</template>
