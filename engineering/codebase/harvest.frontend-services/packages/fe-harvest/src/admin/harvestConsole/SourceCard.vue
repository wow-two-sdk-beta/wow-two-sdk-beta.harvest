<script setup lang="ts">
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import LastHarvestPill from './LastHarvestPill.vue';
import NewPostsBars from './NewPostsBars.vue';
import SourceIcon from './SourceIcon.vue';
import { StateWords, stateTone } from './SourceWords';

/**
 * A source at a glance, as Wheelhouse draws a product: how it is read, its name and state, one pill for the last
 * harvest — when it ran and what it found — and the 14-day chart of new posts at the bottom right. The whole card
 * opens the source through its name's stretched link.
 */
defineOptions({ name: 'SourceCard' });
const props = defineProps<{ source: SourceOverviewModel; trendMax?: number }>();
</script>

<template>
  <article
    class="relative flex min-w-0 flex-col gap-2.5 rounded-2xl border border-border bg-card p-5 transition-[border-color,box-shadow] duration-150 hover:border-primary/40 hover:shadow-sm has-[a:focus-visible]:outline-2 has-[a:focus-visible]:outline-offset-2 has-[a:focus-visible]:outline-ring motion-reduce:transition-none"
    :aria-label="props.source.name"
  >
    <div class="flex min-w-0 items-center gap-3">
      <SourceIcon :family="props.source.family" size="md" />
      <ConsoleLink
        :to="{ kind: 'source', id: props.source.sourceId }"
        class="min-w-0 flex-1 truncate text-[15px] font-semibold outline-none after:absolute after:inset-0 after:rounded-2xl after:content-['']"
      >
        {{ props.source.name }}
      </ConsoleLink>
      <HarvestPill :tone="stateTone(props.source.state)">{{ StateWords[props.source.state] }}</HarvestPill>
    </div>
    <div class="mt-auto flex min-w-0 items-end gap-3 pt-1">
      <LastHarvestPill :harvest="props.source.lastHarvest" />
      <span class="ms-auto flex min-w-12 max-w-36 flex-1 justify-end">
        <NewPostsBars :values="props.source.newPostsDaily" :max="props.trendMax" fill />
      </span>
    </div>
  </article>
</template>
