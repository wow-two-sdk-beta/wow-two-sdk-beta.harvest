<script setup lang="ts">
import { computed, ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Tooltip } from '@wow-two-beta/fe/presentation/overlays';
import type { CatalogSourceModel } from '../../contracts';
import { catalogChannelsText, catalogHostsText, catalogOffText, CatalogKinds } from './AvailableSourceWords';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import { useHarvestConsole } from './HarvestConsole';
import SourceIcon from './SourceIcon.vue';
import type { GalleryView } from './SourceWords';

/**
 * One registered source this instance has not turned on, as a card or a row: how it is read, its name, whether it
 * collects events or listings, its addresses and channels, and **Turn on**. An entry that was turned off adds the day it
 * was and a link to each source it set up, whose posts and settings stay.
 */
defineOptions({ name: 'AvailableSourceItem' });
const props = withDefaults(defineProps<{ entry: CatalogSourceModel; view?: GalleryView }>(), { view: 'cards' });
const session = useHarvestConsole();
const { pending } = session;
const turning = ref(false);
const isRow = computed(() => props.view === 'rows');
const canTurnOn = computed(() => session.canOperate.value && session.canWrite('sources:manage'));
const family = computed(() => props.entry.hosts[0]?.family ?? 'site');
const kind = computed(() => CatalogKinds.find((item) => item.kind === props.entry.kind)?.pill ?? props.entry.kind);
const isOff = computed(() => props.entry.state === 'off');

async function turnOn() {
  if (!canTurnOn.value || pending.value) return;
  turning.value = true;
  try {
    await session.setCatalogSourceTurnedOn(props.entry.key, true);
  } finally {
    turning.value = false;
  }
}
</script>

<template>
  <component
    :is="isRow ? 'li' : 'article'"
    class="grid min-w-0 grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-3"
    :class="
      isRow
        ? 'min-h-14 items-center border-t border-border px-4 py-3 first:border-t-0 sm:grid-cols-[auto_minmax(0,1fr)_auto]'
        : 'content-start rounded-2xl border border-border bg-card p-5'
    "
    :aria-label="props.entry.name"
    :data-state="props.entry.state"
  >
    <SourceIcon :family="family" :size="isRow ? 'sm' : 'md'" decorative />
    <div class="flex min-w-0 flex-col gap-1.5">
      <div class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
        <h3 class="min-w-0 truncate text-[15px] font-semibold" :title="props.entry.name">{{ props.entry.name }}</h3>
        <HarvestPill>{{ kind }}</HarvestPill>
        <HarvestPill v-if="isOff" tone="warning">{{ catalogOffText(props.entry) }}</HarvestPill>
      </div>
      <p v-if="props.entry.description" class="line-clamp-2 text-[13px] leading-snug text-muted-foreground">
        {{ props.entry.description }}
      </p>
      <p class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-muted-foreground">
        <span v-if="catalogHostsText(props.entry)" class="min-w-0 truncate" :title="catalogHostsText(props.entry)">
          {{ catalogHostsText(props.entry) }}
        </span>
        <span v-if="catalogHostsText(props.entry)" aria-hidden="true">·</span>
        <span class="shrink-0 tabular-nums">{{ catalogChannelsText(props.entry) }}</span>
      </p>
      <ul
        v-if="isOff && props.entry.sources.length"
        class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs"
        aria-label="Its sources"
      >
        <li v-for="source in props.entry.sources" :key="source.sourceId" class="min-w-0">
          <ConsoleLink
            :to="{ kind: 'source', id: source.sourceId }"
            class="font-medium text-primary underline-offset-2 hover:underline"
          >
            {{ source.name }}
          </ConsoleLink>
        </li>
      </ul>
    </div>
    <div class="col-span-full flex" :class="isRow ? 'sm:col-span-1 sm:justify-end' : 'mt-auto'">
      <Tooltip
        content="Only an SCFD operator who manages sources can turn a source on."
        :is-disabled="canTurnOn"
        :open-delay="200"
      >
        <span
          :tabindex="canTurnOn ? undefined : 0"
          class="max-sm:w-full max-md:[&>button]:min-h-11 max-sm:[&>button]:w-full rounded-md focus-visible:outline-2 focus-visible:outline-ring"
        >
          <Button
            v-bind="{ 'aria-label': `Turn on ${props.entry.name}` }"
            :is-disabled="!canTurnOn || pending"
            :is-loading="turning"
            @click="turnOn"
          >
            Turn on
          </Button>
        </span>
      </Tooltip>
    </div>
  </component>
</template>
