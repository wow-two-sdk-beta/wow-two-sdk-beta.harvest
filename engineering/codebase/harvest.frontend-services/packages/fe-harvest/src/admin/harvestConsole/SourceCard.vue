<script setup lang="ts">
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import LastHarvestPill from './LastHarvestPill.vue';
import NewPostsBars from './NewPostsBars.vue';
import SourceIcon from './SourceIcon.vue';
import PillIcon from './PillIcon.vue';
import { sourceStateLabel, sourceTypeLabel } from './SourcePresentation';
import { readMethodOf } from './SourceWords';

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
      <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="md" decorative />
      <Tooltip :content="props.source.name" :open-delay="200">
        <ConsoleLink
          :to="{ kind: 'source', id: props.source.sourceId }"
          class="min-w-0 flex-1 truncate text-[15px] font-semibold outline-none after:absolute after:inset-0 after:rounded-2xl after:content-['']"
        >
          {{ props.source.name }}
        </ConsoleLink>
      </Tooltip>
      <HarvestPill :tone="props.source.state === 'needsAttention' ? 'warning' : 'neutral'">
        {{ sourceStateLabel(props.source) }}
      </HarvestPill>
    </div>
    <div class="flex min-w-0 flex-wrap items-center gap-2 text-xs text-muted-foreground">
      <span class="inline-flex items-center gap-1">
        <PillIcon :name="readMethodOf(props.source.family)" />
        {{ sourceTypeLabel(props.source.family) }}
      </span>
      <span v-if="props.source.handle !== props.source.name" class="min-w-0 truncate">{{ props.source.handle }}</span>
    </div>
    <div class="mt-auto flex min-w-0 flex-wrap items-end gap-x-3 gap-y-2 pt-1">
      <LastHarvestPill :harvest="props.source.lastHarvest" :source-id="props.source.sourceId" />
      <span class="ms-auto flex min-w-12 max-w-36 flex-1 justify-end">
        <NewPostsBars :values="props.source.newPostsDaily" :max="props.trendMax" fill />
      </span>
    </div>
  </article>
</template>
