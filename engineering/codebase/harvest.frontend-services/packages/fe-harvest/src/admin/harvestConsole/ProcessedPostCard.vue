<script setup lang="ts">
import { computed } from 'vue';
import { Calendar, MapPin } from 'lucide-vue-next';
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
 * Shows the processed title over the post's media and the extraction beneath it. The title links to the processed
 * detail across the card. The details page carries the post's tags and evidence.
 */
defineOptions({ name: 'ProcessedPostCard' });
const props = defineProps<{
  source: Pick<SourceOverviewModel, 'sourceId' | 'family' | 'faviconUrl'>;
  post: SourceProcessedPostModel;
}>();

const tags = computed(() => props.post.tags);
const extraction = computed(() => props.post.extraction);
const formatIcon = computed(() => eventKindIconOf(extraction.value?.event?.format));
const flags = computed(() => unsureFlags(tags.value, extraction.value));
const flagNames = computed(() => flags.value.map((flag) => flag.field).join(', '));
const summary = computed(() => {
  const value = extraction.value;
  if (!value || value.status !== 'done') return [];
  if (value.event) return eventLines(value.event);
  if (value.listing) return listingLines(value.listing);
  return [];
});
const place = computed(() => ({ kind: 'source', id: props.source.sourceId, processed: props.post.postId }) as const);
const extractionLabel = computed(() =>
  extraction.value?.useCase === 'extract-listing' ? 'Listing extraction' : 'Event extraction',
);

/** The glyph in front of each summary line; a place uses the map pin. */
const LineGlyphs: Record<string, GlyphName | 'place'> = {
  name: 'event',
  place: 'place',
  terms: 'offer',
  languages: 'language',
  offer: 'offer',
  unit: 'all',
};
</script>

<template>
  <article
    class="isolate flex min-w-0 flex-col overflow-hidden rounded-2xl border border-border bg-card hover:border-primary/40 has-[a[data-processed-link]:focus-visible]:outline-2 has-[a[data-processed-link]:focus-visible]:outline-offset-2 has-[a[data-processed-link]:focus-visible]:outline-ring"
  >
    <ProcessedPostHeader :source="props.source" :post="props.post" :to="place" />

    <section
      v-if="extraction && (extraction.role || extraction.status !== 'done' || flags.length || summary.length)"
      class="m-4 grid gap-1.5 rounded-xl bg-muted/50 px-3 py-2.5 text-[13px]"
      :aria-label="extractionLabel"
    >
      <p
        v-if="extraction.role || extraction.status !== 'done' || flags.length"
        class="flex flex-wrap items-center gap-x-1.5 gap-y-1 text-xs font-semibold text-muted-foreground"
      >
        <span v-if="extraction.role">{{ extraction.role }}</span>
        <template v-if="extraction.status !== 'done'">
          <HarvestPill tone="warning">{{ extraction.status.toLowerCase() }}</HarvestPill>
          <span v-if="extraction.error" class="font-normal">{{ extraction.error }}</span>
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
        <Calendar
          v-else-if="line.key === 'dates'"
          :size="12"
          class="shrink-0 text-muted-foreground"
          aria-hidden="true"
        />
        <MapPin v-else-if="LineGlyphs[line.key] === 'place'" :size="13" class="shrink-0 text-muted-foreground" />
        <PillIcon
          v-else-if="LineGlyphs[line.key]"
          :name="LineGlyphs[line.key] as GlyphName"
          class="text-muted-foreground"
        />
        <span class="min-w-0" :class="line.key === 'languages' ? 'break-words' : 'truncate'">{{ line.text }}</span>
        <MapLinks v-if="line.key === 'place'" :query="line.text" class="shrink-0" />
      </p>
    </section>
  </article>
</template>
