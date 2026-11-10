<script setup lang="ts">
import { computed } from 'vue';
import type { SourceOverviewModel } from '../../contracts';
import HarvestPill from './HarvestPill.vue';
import NewPostsBars from './NewPostsBars.vue';
import { useHarvestConsole } from './HarvestConsole';
import { sameSection } from './SourceSections';
import {
  checkWords,
  clockOf,
  harvestWords,
  outcomeWords,
  paceWords,
  plural,
  reasonWords,
  statusTone,
  statusWords,
  trendStartOf,
  whenOf,
} from './SourceWords';

/**
 * A source's Overview: the sections being harvested — how often, the last harvest and the next, their posts and who
 * follows them — with every other section the adapter lists folded into one closed line; then the source's health
 * (reading pace, pause, failures in a row, last refusal) and its 14-day chart of new posts. Schedules change on Settings.
 */
defineOptions({ name: 'SourceOverviewTab' });
const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();

const harvested = computed(() => props.source.sections.filter((section) => section.harvesting));
/** The adapter's sections nobody harvests, and sections whose harvest is off, as one folded list. */
const others = computed(() => {
  const adapter = session.sectionsOf(props.source.sourceId);
  const inAdapter = (url?: string) => !!url && adapter.some((section) => sameSection(url, section.url));
  const pipelineOf = (url: string) =>
    props.source.sections.find((section) => section.url && sameSection(section.url, url));
  return [
    ...adapter
      .filter((section) => !harvested.value.some((row) => row.url && sameSection(row.url, section.url)))
      .map((section) => {
        const existing = pipelineOf(section.url);
        return {
          key: section.url,
          label: section.label,
          note: existing?.paused ? 'paused' : existing ? 'harvest off' : 'not harvested',
        };
      }),
    ...props.source.sections
      .filter((section) => !section.harvesting && !inAdapter(section.url))
      .map((section) => ({
        key: section.pipelineId,
        label: session.sectionLabel(section.pipelineId),
        note: section.paused ? 'paused' : 'harvest off',
      })),
  ];
});
const paused = computed(() => {
  const until = props.source.health.pausedUntil;
  if (!until || new Date(until).getTime() <= Date.now()) return 'no';
  const reason = props.source.health.pauseReason;
  return `until ${clockOf(until)}${reason ? ` · ${outcomeWords(reason)}` : ''}`;
});
const refusal = computed(() => {
  const last = props.source.health.lastRefusal;
  return last ? `${whenOf(last.at)} · ${outcomeWords(last.outcome)}` : 'none';
});
</script>

<template>
  <div class="grid min-w-0 gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
    <section
      class="min-w-0 self-start overflow-hidden rounded-2xl border border-border bg-card"
      aria-label="Sections being harvested"
    >
      <h2 class="px-6 pt-5 pb-3 text-lg font-semibold">Sections being harvested</h2>
      <div class="overflow-x-auto">
        <table class="w-full min-w-[40rem] text-sm">
          <thead>
            <tr class="border-b border-border text-left text-[13px] text-muted-foreground">
              <th class="px-6 py-2.5 font-semibold">Section</th>
              <th class="px-3 py-2.5 font-semibold">Harvests</th>
              <th class="px-3 py-2.5 font-semibold">Last</th>
              <th class="px-3 py-2.5 font-semibold">Next</th>
              <th class="px-3 py-2.5 font-semibold">Posts</th>
              <th class="px-3 py-2.5 pe-6 font-semibold">Followed by</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="section in harvested" :key="section.pipelineId" class="border-b border-border">
              <td class="px-6 py-3.5 font-semibold">{{ session.sectionLabel(section.pipelineId) }}</td>
              <td class="px-3 py-3.5">
                {{ harvestWords(section.captureEvery) }}
                <span v-if="checkWords(section.checkEvery)" class="block text-[13px] text-muted-foreground">
                  {{ checkWords(section.checkEvery) }}
                </span>
              </td>
              <td class="px-3 py-3.5">
                <template v-if="section.lastRun">
                  <span class="inline-flex items-center gap-2">
                    {{ clockOf(section.lastRun.at) }}
                    <HarvestPill :tone="statusTone(section.lastRun.status)">
                      {{ statusWords(section.lastRun.status) }}
                    </HarvestPill>
                  </span>
                  <span
                    v-if="reasonWords(section.lastRun.reason, section.maxPages)"
                    class="block text-[13px] text-muted-foreground"
                  >
                    {{ reasonWords(section.lastRun.reason, section.maxPages) }}
                  </span>
                </template>
                <span v-else class="text-muted-foreground">not yet</span>
              </td>
              <td class="px-3 py-3.5">{{ section.nextDueAt ? clockOf(section.nextDueAt) : '—' }}</td>
              <td class="px-3 py-3.5 tabular-nums">{{ section.posts }}</td>
              <td class="px-3 py-3.5 pe-6">
                {{ section.subscribers ? plural(section.subscribers, 'subscriber') : 'nobody' }}
              </td>
            </tr>
            <tr v-if="!harvested.length" class="border-b border-border">
              <td colspan="6" class="px-6 py-4 text-muted-foreground">
                No section is harvested yet. An operator turns one on in Settings.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <details v-if="others.length" class="group bg-muted/40" v-bind="{ 'data-folded-sections': '' }">
        <summary
          class="flex cursor-pointer list-none items-center gap-2 px-6 py-3.5 text-sm font-semibold text-muted-foreground hover:text-foreground [&::-webkit-details-marker]:hidden"
        >
          <svg
            width="10"
            height="10"
            viewBox="0 0 24 24"
            class="transition-transform group-open:rotate-90"
            aria-hidden="true"
          >
            <path d="M8 4l10 8-10 8z" fill="currentColor" />
          </svg>
          {{ others.length }} more {{ others.length === 1 ? 'section' : 'sections' }} nobody follows
        </summary>
        <ul class="divide-y divide-border border-t border-border" aria-label="Sections not harvested">
          <li
            v-for="item in others"
            :key="item.key"
            class="flex items-center justify-between gap-3 px-6 py-2.5 text-sm"
          >
            <span class="font-medium">{{ item.label }}</span>
            <span class="text-[13px] text-muted-foreground">{{ item.note }}</span>
          </li>
        </ul>
      </details>
    </section>

    <div class="flex min-w-0 flex-col gap-5">
      <section class="rounded-2xl border border-border bg-card p-6" aria-label="Health">
        <h2 class="text-lg font-semibold">Health</h2>
        <dl class="mt-3 grid grid-cols-[auto_minmax(0,1fr)] gap-x-6 gap-y-2.5 text-[15px]">
          <dt class="text-muted-foreground">Reading pace</dt>
          <dd class="font-semibold">{{ paceWords(props.source.health.minDelayMs) }}</dd>
          <dt class="text-muted-foreground">Paused</dt>
          <dd class="font-semibold">{{ paused }}</dd>
          <dt class="text-muted-foreground">Failures in a row</dt>
          <dd class="font-semibold tabular-nums">{{ props.source.health.consecutiveFailures }}</dd>
          <dt class="text-muted-foreground">Last refusal</dt>
          <dd class="font-semibold" :title="props.source.health.lastRefusal?.message">{{ refusal }}</dd>
        </dl>
      </section>
      <section class="rounded-2xl border border-border bg-card p-6" aria-label="New posts, 14 days">
        <h2 class="text-lg font-semibold">New posts, 14 days</h2>
        <div class="mt-4">
          <NewPostsBars :values="props.source.newPostsDaily" :height="96" :bar-width="8" fill />
        </div>
        <div class="mt-2 flex justify-between text-[13px] text-muted-foreground">
          <span>{{ trendStartOf(props.source.newPostsDaily.length || 14) }}</span>
          <span>today</span>
        </div>
      </section>
    </div>
  </div>
</template>
