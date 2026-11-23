<script setup lang="ts">
import { computed } from 'vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import { kindGlyphOf, readMethodGlyphOf, sectionGlyphOf } from './PostGlyphs';
import type { PostLabel } from './PostLabels';
import PostLanguagePill from './PostLanguagePill.vue';
import { readMethodOf } from './SourceWords';

/**
 * Post metadata pills for its source or section, language and removal state. The detail page can also supply a kind.
 * `onImage` draws them in glass for a card's blurred panel. `compact` writes the language as its code.
 */
defineOptions({ name: 'PostPills' });
const props = withDefaults(
  defineProps<{
    kind?: string;
    pipelineId?: string;
    publisherCategory?: string | null;
    language?: string;
    /** The source's name and family, to add a pill for it. */
    sourceName?: string;
    family?: string;
    removed?: boolean;
    /** Labels to leave out because the list's filter already states them. */
    hidden?: ReadonlySet<PostLabel>;
    onImage?: boolean;
    compact?: boolean;
  }>(),
  {
    kind: undefined,
    pipelineId: undefined,
    publisherCategory: undefined,
    language: undefined,
    sourceName: undefined,
    family: 'site',
    removed: false,
    hidden: undefined,
    onImage: false,
    compact: false,
  },
);
const session = useHarvestConsole();
const section = computed(() => (props.pipelineId ? session.sectionParts(props.pipelineId) : undefined));
const shows = (label: PostLabel) => !props.hidden?.has(label);
const kind = computed(() => (shows('kind') ? props.kind : undefined));
const category = computed(() => {
  if (!shows('section')) return undefined;
  return props.publisherCategory === undefined ? section.value?.name : props.publisherCategory;
});
const language = computed(() => {
  const locale = props.language ?? section.value?.locale;
  return shows('language') && locale !== 'und' ? locale : undefined;
});
const removed = computed(() => props.removed && shows('removed'));
const sourceGlyph = computed(() => readMethodGlyphOf(readMethodOf(props.family)));
const tone = computed(() => (props.onImage ? 'glass' : 'outline'));
</script>

<template>
  <div
    v-if="kind || category || language || props.sourceName || removed"
    class="flex min-h-6 min-w-0 flex-nowrap items-center gap-1.5"
  >
    <HarvestPill v-if="kind" :tone="tone">
      <PillIcon :name="kindGlyphOf(kind)" />
      {{ kind }}
    </HarvestPill>
    <HarvestPill v-if="category" :tone="tone" shrinkable>
      <PillIcon :name="props.publisherCategory ? sectionGlyphOf(category) : sourceGlyph" />
      <span class="truncate">{{ category.toLowerCase() }}</span>
    </HarvestPill>
    <PostLanguagePill v-if="language" :language="language" :tone="tone" :compact="props.compact" />
    <HarvestPill v-if="props.sourceName" :tone="tone" shrinkable>
      <PillIcon :name="sourceGlyph" />
      <span class="truncate">{{ props.sourceName.toLowerCase() }}</span>
    </HarvestPill>
    <HarvestPill v-if="removed" :tone="tone">
      <PillIcon name="unlink" />
      removed
    </HarvestPill>
  </div>
</template>
