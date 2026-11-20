<script setup lang="ts">
import { computed, ref } from 'vue';
import { MapPin } from 'lucide-vue-next';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel, SourceProcessedPostModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import ProviderMark from './ProviderMark.vue';
import SourceIcon from './SourceIcon.vue';
import SourceImage from './SourceImage.vue';
import { kindGlyphOf, type GlyphName } from './PostGlyphs';
import {
  cardModel,
  cardProvider,
  entityTypeWords,
  eventLines,
  listingLines,
  provenanceLines,
  unsureFlags,
  VerdictWords,
  verdictTone,
} from './ProcessedWords';
import { languageName } from './SourceSections';
import { agoOf, imageSrcOf, type PillTone } from './SourceWords';

/**
 * One post as the AI passes read it, in the Posts card's look: a header with the post's first image (a tile with the
 * source icon when it has none), the entity type, relevance verdict and language as glass pills, the answering AI's
 * brand mark and the title over the image's blurred fade. The brand mark's hover lists each pass with its model, cost,
 * speed and tokens, and when it answered; the card carries no other AI stats. Below the header the tags — primary
 * domain, other domains, topics, timeframe — what the post is about and, for an extracted event or listing, a compact
 * summary whose top row ends with a `? N unsure` pill that lists the fields the model was unsure of. Evidence quotes
 * stay on the post's own page, which the title opens.
 */
defineOptions({ name: 'ProcessedPostCard' });
const props = defineProps<{
  source: Pick<SourceOverviewModel, 'sourceId' | 'family' | 'faviconUrl'>;
  post: SourceProcessedPostModel;
}>();

const DomainLimit = 2;
const TopicLimit = 4;
const tags = computed(() => props.post.tags);
const extraction = computed(() => props.post.extraction);
const failedImage = ref(false);
const image = computed(() => (failedImage.value ? undefined : imageSrcOf(props.post.imageUrl)));
const primary = computed(() => tags.value.domains.find((domain) => domain.primary));
const others = computed(() => tags.value.domains.filter((domain) => !domain.primary));
const shownDomains = computed(() => others.value.slice(0, DomainLimit));
const hiddenDomains = computed(() => others.value.slice(DomainLimit));
const shownTopics = computed(() => tags.value.topics.slice(0, TopicLimit));
const hiddenTopics = computed(() => tags.value.topics.slice(TopicLimit));
const flags = computed(() => unsureFlags(tags.value, extraction.value));
const flagNames = computed(() => flags.value.map((flag) => flag.field).join(', '));
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
const place = computed(
  () => ({ kind: 'source', id: props.source.sourceId, tab: 'posts', post: props.post.postId }) as const,
);
const removed = computed(() => props.post.state === 'gone');
const extractionLabel = computed(() =>
  extraction.value?.useCase === 'extract-listing' ? 'Listing extraction' : 'Event extraction',
);

/** The glyph in front of each summary line; a place uses the map pin. */
const LineGlyphs: Record<string, GlyphName | 'place'> = {
  name: 'event',
  dates: 'dates',
  place: 'place',
  terms: 'offer',
  offer: 'offer',
  unit: 'all',
};
</script>

<template>
  <article
    class="flex min-w-0 flex-col overflow-hidden rounded-2xl border border-border bg-card hover:border-primary/40"
    :aria-label="props.post.title"
  >
    <header
      class="relative isolate flex min-w-0 flex-col gap-3 overflow-hidden p-4"
      :class="
        image
          ? 'aspect-[16/9] justify-between bg-muted text-white'
          : 'min-h-20 bg-gradient-to-br from-primary-soft to-muted text-foreground'
      "
    >
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
      <ul class="z-10 flex min-w-0 flex-wrap items-center gap-1.5 pe-10" aria-label="Entity type, verdict and language">
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
      </ul>
      <div class="relative isolate grid min-w-0 gap-0.5">
        <div
          v-if="image"
          class="pointer-events-none absolute -inset-x-4 -top-8 -bottom-4 -z-10 bg-gradient-to-t from-black/80 via-black/45 to-transparent backdrop-blur-xl [-webkit-mask-image:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)] [mask-image:linear-gradient(to_top,black_calc(100%_-_2rem),transparent)]"
          aria-hidden="true"
        ></div>
        <ConsoleLink :to="place" class="line-clamp-2 text-start text-[15px] leading-snug font-semibold hover:underline">
          {{ props.post.title }}
        </ConsoleLink>
        <p class="line-clamp-2 text-[13px] leading-5" :class="image ? 'text-white/75' : 'text-muted-foreground'">
          {{ subline }}
        </p>
      </div>
      <div class="absolute end-3 top-3 z-20">
        <ProviderMark
          :provider="cardProvider(tags, extraction)"
          :model="cardModel(tags, extraction)"
          :lines="lines"
          :on-image="!!image"
        />
      </div>
    </header>

    <div class="grid gap-3 p-4">
      <p v-if="tags.status !== 'done'" class="text-sm text-destructive" role="status">
        Tagging {{ tags.status }}
        <template v-if="tags.error">· {{ tags.error }}</template>
      </p>
      <template v-else>
        <ul
          v-if="primary || others.length || tags.topics.length || tags.timeframe || !tags.current"
          class="flex min-w-0 flex-wrap items-center gap-1.5"
          aria-label="Tags"
        >
          <li v-if="primary">
            <HarvestPill tone="outline" shrinkable v-bind="{ title: primary.key }">{{ primary.label }}</HarvestPill>
          </li>
          <li v-for="domain in shownDomains" :key="domain.key">
            <HarvestPill shrinkable v-bind="{ title: domain.key }">{{ domain.label }}</HarvestPill>
          </li>
          <li v-if="hiddenDomains.length">
            <Tooltip :open-delay="150">
              <HarvestPill
                v-bind="{ tabindex: 0, 'aria-label': `More domains: ${hiddenDomains.map((d) => d.label).join(', ')}` }"
              >
                +{{ hiddenDomains.length }}
              </HarvestPill>
              <template #content>
                <span class="grid gap-0.5 text-xs leading-4">
                  <span v-for="domain in hiddenDomains" :key="domain.key">{{ domain.label }}</span>
                </span>
              </template>
            </Tooltip>
          </li>
          <li v-for="topic in shownTopics" :key="topic">
            <HarvestPill tone="dashed" shrinkable>#{{ topic }}</HarvestPill>
          </li>
          <li v-if="hiddenTopics.length">
            <Tooltip :open-delay="150">
              <HarvestPill
                tone="dashed"
                v-bind="{ tabindex: 0, 'aria-label': `More topics: ${hiddenTopics.join(', ')}` }"
              >
                +{{ hiddenTopics.length }}
              </HarvestPill>
              <template #content>
                <span class="grid gap-0.5 text-xs leading-4">
                  <span v-for="topic in hiddenTopics" :key="topic">#{{ topic }}</span>
                </span>
              </template>
            </Tooltip>
          </li>
          <li v-if="tags.timeframe">
            <HarvestPill tone="dashed">
              <PillIcon name="clock" />
              {{ tags.timeframe }}
            </HarvestPill>
          </li>
          <li v-if="!tags.current">
            <HarvestPill tone="warning">tags from an earlier text</HarvestPill>
          </li>
        </ul>
        <p v-if="tags.about" class="line-clamp-3 text-sm">{{ tags.about }}</p>
      </template>

      <section
        v-if="extraction || flags.length"
        class="grid gap-1.5 text-[13px]"
        :class="extraction ? 'rounded-xl bg-muted/50 px-3 py-2.5' : ''"
        :aria-label="extraction ? extractionLabel : undefined"
      >
        <p class="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs font-semibold text-muted-foreground">
          <template v-if="extraction">
            <span>{{ extraction.useCase === 'extract-listing' ? 'Listing' : 'Event' }}</span>
            <span v-if="extraction.role">· {{ extraction.role }}</span>
            <template v-if="extraction.status !== 'done'">
              <HarvestPill tone="warning">{{ extraction.status }}</HarvestPill>
              <span v-if="extraction.error" class="font-normal">{{ extraction.error }}</span>
            </template>
            <span v-else>· done</span>
          </template>
          <Tooltip v-if="flags.length" :open-delay="150" placement="top-end">
            <HarvestPill
              tone="warning"
              class="ms-auto"
              v-bind="{ tabindex: 0, role: 'img', 'aria-label': `${flags.length} unsure: ${flagNames}` }"
            >
              ? {{ flags.length }} unsure
            </HarvestPill>
            <template #content>
              <span class="grid max-w-72 gap-1 text-xs leading-4">
                <span v-for="flag in flags" :key="flag.field" class="grid">
                  <span class="font-semibold">{{ flag.field }}</span>
                  <span>
                    {{ flag.reason }}
                    <template v-if="flag.proposed">→ {{ flag.proposed }}</template>
                  </span>
                </span>
              </span>
            </template>
          </Tooltip>
        </p>
        <p
          v-for="line in summary"
          :key="line.key"
          class="flex min-w-0 items-center gap-1.5"
          v-bind="{ 'data-line': line.key }"
        >
          <MapPin v-if="LineGlyphs[line.key] === 'place'" :size="13" class="shrink-0 text-muted-foreground" />
          <PillIcon
            v-else-if="LineGlyphs[line.key]"
            :name="LineGlyphs[line.key] as GlyphName"
            class="text-muted-foreground"
          />
          <span class="min-w-0 truncate">{{ line.text }}</span>
        </p>
      </section>
    </div>
  </article>
</template>
