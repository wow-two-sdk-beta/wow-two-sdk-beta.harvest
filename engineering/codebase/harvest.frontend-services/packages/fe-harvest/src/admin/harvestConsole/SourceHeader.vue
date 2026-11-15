<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import PillIcon from './PillIcon.vue';
import SourceIcon from './SourceIcon.vue';
import { useHarvestConsole } from './HarvestConsole';
import {
  agoOf,
  clockOf,
  lastHarvestNote,
  lastHarvestTone,
  nextHarvestWords,
  siteUrlOf,
  StateWords,
  statusWords,
  webUrlOf,
} from './SourceWords';

/**
 * Names the source above its tabs, as Wheelhouse's product header does: how it is read, its name and state, the last
 * and next harvest, its group, and actions. "Harvest now" queues one harvest per harvested section, for operators only.
 */
defineOptions({ name: 'SourceHeader' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const { pending, canOperate, canWrite } = session;
const now = ref(new Date());
let timer: ReturnType<typeof setInterval> | undefined;
onMounted(() => {
  timer = setInterval(() => (now.value = new Date()), 15_000);
});
onUnmounted(() => clearInterval(timer));

const group = computed(() => session.groupOf(props.source.groupId));
const harvested = computed(() => props.source.sections.filter((section) => section.harvesting));
const site = computed(() => {
  const url = webUrlOf(siteUrlOf(props.source.family, props.source.handle));
  return url && props.source.family === 'site' ? new URL('/', url).href : url;
});
const last = computed(() => props.source.lastHarvest);
const lastText = computed(() => {
  if (!last.value) return 'No harvest yet';
  if (last.value.status === 'running') return 'Harvesting now';
  if (last.value.status === 'queued') return 'Harvest queued';
  const time = `Last harvest ${agoOf(last.value.at, now.value)}`;
  return last.value.status === 'completed' ? time : `${time} · ${statusWords(last.value.status)}`;
});
const lastTitle = computed(() =>
  last.value ? [clockOf(last.value.at), lastHarvestNote(last.value)].filter(Boolean).join(' · ') : undefined,
);
const canHarvest = computed(() => canOperate.value && canWrite('pipelines:run'));
</script>

<template>
  <header class="@container rounded-2xl border border-border bg-card p-4 sm:p-6" aria-label="Source">
    <div class="grid min-w-0 items-start gap-4 @min-[42rem]:grid-cols-[minmax(0,1fr)_auto]">
      <div class="grid min-w-0 grid-cols-[auto_minmax(0,1fr)] items-center gap-x-4 gap-y-3">
        <SourceIcon :family="props.source.family" :favicon-url="props.source.faviconUrl" size="lg" />
        <h1 class="flex min-w-0 flex-wrap items-center gap-2.5 text-2xl font-semibold tracking-tight">
          <span class="min-w-0 wrap-anywhere">{{ props.source.name }}</span>
          <HarvestPill>{{ StateWords[props.source.state] }}</HarvestPill>
        </h1>
        <div class="col-span-full flex min-w-0 flex-wrap items-center gap-2 @min-[36rem]:col-start-2">
          <ConsoleLink
            v-if="group"
            :to="{ kind: 'sources', group: group.id }"
            class="inline-flex min-h-7 min-w-0 items-center gap-1.5 rounded-full border border-border bg-card px-3 text-[13px] font-semibold text-muted-foreground hover:border-primary/40 hover:text-foreground"
            v-bind="{ title: `All sources of ${group.name}` }"
          >
            <PillIcon name="all" />
            <span class="truncate">{{ group.name }}</span>
          </ConsoleLink>
          <HarvestPill :tone="lastHarvestTone(last)" size="md" v-bind="{ title: lastTitle }">
            <PillIcon name="history" />
            {{ lastText }}
          </HarvestPill>
          <HarvestPill
            :tone="props.source.nextHarvestAt ? 'outline' : 'dashed'"
            size="md"
            v-bind="{ title: props.source.nextHarvestAt ? clockOf(props.source.nextHarvestAt) : undefined }"
          >
            <PillIcon name="clock" />
            {{ nextHarvestWords(props.source.nextHarvestAt, now) }}
          </HarvestPill>
        </div>
      </div>
      <div
        v-if="site || canHarvest"
        class="flex flex-wrap items-center gap-2.5"
        role="group"
        aria-label="Source actions"
      >
        <Button v-if="site" as-child variant="outline">
          <a :href="site" target="_blank" rel="noopener noreferrer">
            Open the site
            <PillIcon name="external" />
          </a>
        </Button>
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
