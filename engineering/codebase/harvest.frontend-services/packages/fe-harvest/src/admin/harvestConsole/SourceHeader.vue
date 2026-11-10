<script setup lang="ts">
import { computed, ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Popover, PopoverContent, PopoverTrigger } from '@wow-two-beta/fe/presentation/overlays';
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import SourceIcon from './SourceIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import {
  harvestTimesText,
  lastHarvestText,
  lastHarvestTone,
  shortUrl,
  siteUrlOf,
  StateWords,
  stateTone,
} from './SourceWords';

/**
 * Names the source above its tabs, as Wheelhouse's product header does: how it is read, its name and state, the last
 * and next harvest, its group and what the last harvest found. "Open the site" lists the site and the sections being
 * harvested; "Harvest now" queues one harvest per harvested section, for operators only.
 */
defineOptions({ name: 'SourceHeader' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const { pending, canOperate, canWrite } = session;
const open = ref(false);

const group = computed(() => session.groupOf(props.source.groupId));
const harvested = computed(() => props.source.sections.filter((section) => section.harvesting));
const site = computed(() => siteUrlOf(props.source.family, props.source.handle));
const links = computed(() => [
  ...(site.value ? [{ key: 'site', label: 'The site', url: site.value }] : []),
  ...harvested.value
    .filter((section) => section.url)
    .map((section) => ({
      key: section.pipelineId,
      label: session.sectionLabel(section.pipelineId),
      url: section.url!,
    })),
]);
const canHarvest = computed(() => canOperate.value && canWrite('pipelines:run'));
</script>

<template>
  <header class="rounded-2xl border border-border bg-card p-5 sm:p-6" aria-label="Source">
    <div class="flex min-w-0 flex-wrap items-start gap-4">
      <SourceIcon :family="props.source.family" size="lg" />
      <div class="flex min-w-0 flex-1 flex-col gap-1.5">
        <h1 class="flex min-w-0 flex-wrap items-center gap-2.5 text-2xl font-semibold tracking-tight">
          <span class="truncate">{{ props.source.name }}</span>
          <HarvestPill :tone="stateTone(props.source.state)">{{ StateWords[props.source.state] }}</HarvestPill>
        </h1>
        <p class="text-[15px] text-muted-foreground">{{ harvestTimesText(props.source) }}</p>
        <div class="mt-1 flex flex-wrap items-center gap-2">
          <ConsoleLink
            v-if="group"
            :to="{ kind: 'sources', group: group.id }"
            class="inline-flex h-7 items-center gap-1.5 rounded-full border border-border bg-card px-3 text-[13px] font-semibold text-muted-foreground hover:border-primary/40 hover:text-foreground"
            v-bind="{ title: `All sources of ${group.name}` }"
          >
            <svg width="12" height="12" viewBox="0 0 24 24" aria-hidden="true">
              <path d="M4 7h16M4 12h16M4 17h16" fill="none" stroke="currentColor" stroke-width="2.2" />
            </svg>
            {{ group.name }}
          </ConsoleLink>
          <HarvestPill
            :tone="lastHarvestTone(props.source.lastHarvest)"
            size="md"
            v-bind="{ 'data-pill': 'last-harvest' }"
          >
            {{ lastHarvestText(props.source.lastHarvest) }}
          </HarvestPill>
        </div>
      </div>
      <div class="flex shrink-0 flex-wrap items-center gap-2.5" role="group" aria-label="Source actions">
        <Popover v-if="links.length" v-model:open="open" placement="bottom-end">
          <PopoverTrigger as-child>
            <Button variant="outline">
              Open the site
              <svg width="12" height="12" viewBox="0 0 24 24" aria-hidden="true">
                <path d="M6 9l6 6 6-6" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" />
              </svg>
            </Button>
          </PopoverTrigger>
          <PopoverContent
            class="w-[min(22rem,calc(100vw-2rem))] p-1.5 text-sm"
            v-bind="{ 'aria-label': `Open ${props.source.name}` }"
          >
            <ul class="flex flex-col">
              <li v-for="link in links" :key="link.key">
                <a
                  :href="link.url"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="flex min-h-11 flex-col justify-center rounded-lg px-2.5 py-1.5 hover:bg-muted/60 focus-visible:outline-2 focus-visible:outline-ring"
                  @click="open = false"
                >
                  <span class="font-semibold">{{ link.label }}</span>
                  <span class="truncate text-xs text-muted-foreground">{{ shortUrl(link.url) }}</span>
                </a>
              </li>
            </ul>
          </PopoverContent>
        </Popover>
        <Button
          v-if="canHarvest"
          :is-loading="pending"
          :is-disabled="!harvested.length"
          :title="harvested.length ? 'One harvest for every section being harvested' : 'Turn a section on first'"
          @click="session.harvestNow(props.source.sourceId)"
        >
          Harvest now
        </Button>
      </div>
    </div>
  </header>
</template>
