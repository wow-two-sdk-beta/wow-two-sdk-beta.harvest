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

/** A source in one line of its group's list: icon, name, the last-harvest pill, its state and trend. */
defineOptions({ name: 'SourceRow' });
const props = defineProps<{ source: SourceOverviewModel; trendMax?: number }>();
</script>

<template>
  <li
    class="relative grid min-h-14 min-w-0 grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-2 border-t border-border px-4 py-2 transition-colors first:border-t-0 hover:bg-muted/40 has-[a:focus-visible]:outline-2 has-[a:focus-visible]:-outline-offset-2 has-[a:focus-visible]:outline-ring"
    :aria-label="props.source.name"
  >
    <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="sm" decorative />
    <Tooltip :content="props.source.name" :open-delay="200">
      <ConsoleLink
        :to="{ kind: 'source', id: props.source.sourceId }"
        class="min-w-0 truncate text-sm font-semibold outline-none after:absolute after:inset-0 after:content-['']"
      >
        {{ props.source.name }}
      </ConsoleLink>
    </Tooltip>
    <HarvestPill :tone="props.source.state === 'needsAttention' ? 'warning' : 'neutral'">
      {{ sourceStateLabel(props.source) }}
    </HarvestPill>
    <div class="col-span-full flex min-w-0 flex-wrap items-center gap-x-3 gap-y-2 sm:col-start-2">
      <span class="inline-flex items-center gap-1 text-xs text-muted-foreground">
        <PillIcon :name="readMethodOf(props.source.family)" />
        {{ sourceTypeLabel(props.source.family) }}
      </span>
      <LastHarvestPill :harvest="props.source.lastHarvest" :source-id="props.source.sourceId" />
      <span class="ms-auto flex w-28 max-w-full justify-end">
        <NewPostsBars :values="props.source.newPostsDaily" :max="props.trendMax" fill />
      </span>
    </div>
  </li>
</template>
