<script setup lang="ts">
import { computed } from 'vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import { kindGlyphOf, readMethodGlyphOf, sectionGlyphOf } from './PostGlyphs';
import { languageName } from './SourceSections';
import { readMethodOf } from './SourceWords';

/**
 * The pills of a post, each with a line glyph: its kind (a calendar for an event, a page for an article), its section
 * (a frame for exhibitions, a hanger for fashion) and the section's language as a pill of its own, the source it was
 * read from when asked, and `removed` once its source dropped it. In lower case throughout. `onImage` draws them in
 * glass for a card's blurred panel; `compact` writes the language as its code.
 */
defineOptions({ name: 'PostPills' });
const props = withDefaults(
  defineProps<{
    kind: string;
    pipelineId?: string;
    /** The source's name and family, to add a pill for it. */
    sourceName?: string;
    family?: string;
    removed?: boolean;
    onImage?: boolean;
    compact?: boolean;
  }>(),
  { pipelineId: undefined, sourceName: undefined, family: 'site', removed: false, onImage: false, compact: false },
);
const session = useHarvestConsole();
const section = computed(() => (props.pipelineId ? session.sectionParts(props.pipelineId) : undefined));
const kindTone = computed(() =>
  props.onImage ? (props.kind === 'event' ? 'glassSuccess' : 'glass') : props.kind === 'event' ? 'success' : 'outline',
);
const quietTone = computed(() => (props.onImage ? 'glass' : 'outline'));
</script>

<template>
  <div class="flex min-w-0 flex-nowrap items-center gap-1.5">
    <HarvestPill :tone="kindTone">
      <PillIcon :name="kindGlyphOf(props.kind)" />
      {{ props.kind }}
    </HarvestPill>
    <HarvestPill v-if="section" :tone="quietTone" shrinkable>
      <PillIcon :name="sectionGlyphOf(section.name)" />
      <span class="truncate">{{ section.name.toLowerCase() }}</span>
    </HarvestPill>
    <HarvestPill v-if="section?.locale" :tone="quietTone" v-bind="{ title: languageName(section.locale) }">
      <PillIcon name="language" />
      {{ (props.compact ? section.locale : languageName(section.locale)).toLowerCase() }}
    </HarvestPill>
    <HarvestPill v-if="props.sourceName" :tone="quietTone" shrinkable>
      <PillIcon :name="readMethodGlyphOf(readMethodOf(props.family))" />
      <span class="truncate">{{ props.sourceName.toLowerCase() }}</span>
    </HarvestPill>
    <HarvestPill v-if="props.removed" :tone="props.onImage ? 'glass' : 'danger'">
      <PillIcon name="other" />
      removed
    </HarvestPill>
  </div>
</template>
