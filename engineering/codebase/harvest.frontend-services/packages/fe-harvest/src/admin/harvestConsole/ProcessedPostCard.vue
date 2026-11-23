<script setup lang="ts">
import { computed } from 'vue';
import { MapPin } from 'lucide-vue-next';
import { MapLinks } from '@wow-two-beta/fe/domain/maps';
import { Icon } from '@wow-two-beta/fe/foundation/icons';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel, SourceProcessedPostModel } from '../../contracts';
import { eventKindIconOf } from './EventKindIcons';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import ProcessedPostHeader from './ProcessedPostHeader.vue';
import type { GlyphName } from './PostGlyphs';
import { eventLines, listingLines, unsureFlags } from './ProcessedWords';

/**
 * One post as the AI passes read it, in the Posts card's look: the shared media header (the post's first image or a
 * source-icon tile, the entity type, relevance verdict and language as glass pills with the answering AI's brand mark
 * last, the ↗ open-the-original button top right, and the title over the image's blurred fade), then the tags — primary
 * domain, other domains, topics, timeframe — what the post is about and, for an extracted event or listing, a compact
 * summary whose top row names the post's role (a pass that did not finish shows a warning pill instead) and ends with a
 * `? N unsure` pill that lists the fields the model was unsure of. The brand mark's hover lists each pass with its model,
 * cost, speed and tokens, and when it answered; the card carries no other AI stats. The whole card is one link to the
 * processed post's page, stretched from the title's link; the pills, the ↗ button and the unsure pill stay above it.
 * Evidence quotes live on that page.
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
const formatIcon = computed(() => eventKindIconOf(extraction.value?.event?.format));
const primary = computed(() => tags.value.domains.find((domain) => domain.primary));
const others = computed(() => tags.value.domains.filter((domain) => !domain.primary));
const shownDomains = computed(() => others.value.slice(0, DomainLimit));
const hiddenDomains = computed(() => others.value.slice(DomainLimit));
const shownTopics = computed(() => tags.value.topics.slice(0, TopicLimit));
const hiddenTopics = computed(() => tags.value.topics.slice(TopicLimit));
const flags = computed(() => unsureFlags(tags.value, extraction.value));
const flagNames = computed(() => flags.value.map((flag) => flag.field).join(', '));
const summary = computed(() => {
  const value = extraction.value;
  if (!value || value.status !== 'done') return [];
  if (value.event) return eventLines(value.event);
  if (value.listing) return listingLines(value.listing);
  return [];
});
/** Whether the extraction box has anything to say: a role, a pass that did not finish, summary lines or unsure fields. */
const hasExtractionBox = computed(
  () =>
    flags.value.length > 0 ||
    (!!extraction.value && (!!extraction.value.role || extraction.value.status !== 'done' || summary.value.length > 0)),
);
const place = computed(() => ({ kind: 'source', id: props.source.sourceId, processed: props.post.postId }) as const);
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
    class="isolate flex min-w-0 flex-col overflow-hidden rounded-2xl border border-border bg-card hover:border-primary/40 has-[a[data-processed-link]:focus-visible]:outline-2 has-[a[data-processed-link]:focus-visible]:outline-offset-2 has-[a[data-processed-link]:focus-visible]:outline-ring"
    :aria-label="props.post.title"
  >
    <ProcessedPostHeader :source="props.source" :post="props.post" :to="place" />

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
          <li v-if="primary" class="relative z-10">
            <HarvestPill tone="outline" shrinkable v-bind="{ title: primary.key }">{{ primary.label }}</HarvestPill>
          </li>
          <li v-for="domain in shownDomains" :key="domain.key" class="relative z-10">
            <HarvestPill shrinkable v-bind="{ title: domain.key }">{{ domain.label }}</HarvestPill>
          </li>
          <li v-if="hiddenDomains.length" class="relative z-10">
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
          <li v-for="topic in shownTopics" :key="topic" class="relative z-10">
            <HarvestPill tone="dashed" shrinkable>#{{ topic }}</HarvestPill>
          </li>
          <li v-if="hiddenTopics.length" class="relative z-10">
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
          <li v-if="tags.timeframe" class="relative z-10">
            <HarvestPill tone="dashed">
              <PillIcon name="clock" />
              {{ tags.timeframe }}
            </HarvestPill>
          </li>
          <li v-if="!tags.current" class="relative z-10">
            <HarvestPill tone="warning">tags from an earlier text</HarvestPill>
          </li>
        </ul>
        <p v-if="tags.about" class="line-clamp-3 text-sm">{{ tags.about }}</p>
      </template>

      <section
        v-if="hasExtractionBox"
        class="grid gap-1.5 text-[13px]"
        :class="extraction ? 'rounded-xl bg-muted/50 px-3 py-2.5' : ''"
        :aria-label="extraction ? extractionLabel : undefined"
      >
        <p
          v-if="(extraction && (extraction.role || extraction.status !== 'done')) || flags.length"
          class="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs font-semibold text-muted-foreground"
        >
          <template v-if="extraction">
            <span v-if="extraction.role">{{ extraction.role }}</span>
            <template v-if="extraction.status !== 'done'">
              <HarvestPill tone="warning">{{ extraction.status }}</HarvestPill>
              <span v-if="extraction.error" class="font-normal">{{ extraction.error }}</span>
            </template>
          </template>
          <Tooltip v-if="flags.length" :open-delay="150" placement="top-end">
            <HarvestPill
              tone="warning"
              class="relative z-10 ms-auto"
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
          <Icon
            v-if="line.key === 'terms' && formatIcon"
            :icon="formatIcon"
            :size="13"
            class="shrink-0 text-muted-foreground"
          />
          <MapPin v-else-if="LineGlyphs[line.key] === 'place'" :size="13" class="shrink-0 text-muted-foreground" />
          <PillIcon
            v-else-if="LineGlyphs[line.key]"
            :name="LineGlyphs[line.key] as GlyphName"
            class="text-muted-foreground"
          />
          <span class="min-w-0 truncate">{{ line.text }}</span>
          <MapLinks v-if="line.key === 'place'" :query="line.text" class="shrink-0" />
        </p>
      </section>
    </div>
  </article>
</template>
