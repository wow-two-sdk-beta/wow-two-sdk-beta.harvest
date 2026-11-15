<script setup lang="ts">
import { computed } from 'vue';
import type { SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import NewPostsBars from './NewPostsBars.vue';
import PillIcon from './PillIcon.vue';
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
  statusWords,
  trendStartOf,
  webUrlOf,
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
const activeSections = computed(() =>
  harvested.value.map((section) => {
    const summary = section.lastRun;
    if (!summary?.kind || summary.kind === 'capture') return { section, last: summary };
    const run = session.runsOf(section.pipelineId).find((item) => item.kind === 'capture');
    return {
      section,
      last: run
        ? {
            at: run.finishedAtUtc ?? run.queuedAtUtc,
            status: run.status,
            reason: run.completionReason,
            newPosts: run.newPosts ?? run.captured,
            found: run.discovered,
          }
        : undefined,
    };
  }),
);
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
          url: webUrlOf(section.url),
          pipelineId: existing?.pipelineId,
          subscribers: existing?.subscribers ?? 0,
          note: existing?.paused ? 'paused' : existing ? 'harvest off' : 'not harvested',
        };
      }),
    ...props.source.sections
      .filter((section) => !section.harvesting && !inAdapter(section.url))
      .map((section) => ({
        key: section.pipelineId,
        label: session.sectionLabel(section.pipelineId),
        url: webUrlOf(section.url),
        pipelineId: section.pipelineId,
        subscribers: section.subscribers,
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
  <div class="@container/overview">
    <div class="grid min-w-0 gap-5 @min-[67rem]/overview:grid-cols-[minmax(0,1fr)_minmax(18rem,30%)]">
      <section
        class="@container/sections min-w-0 self-start rounded-2xl border border-border bg-card"
        aria-label="Sections being harvested"
      >
        <h2 class="px-4 pt-5 pb-3 text-lg font-semibold sm:px-6">Sections being harvested</h2>
        <table class="block w-full text-sm @min-[43rem]/sections:table" role="table">
          <thead class="@max-[43rem]/sections:sr-only" role="rowgroup">
            <tr class="border-b border-border text-left text-[13px] text-muted-foreground">
              <th class="px-6 py-2.5 font-semibold">Section</th>
              <th class="px-3 py-2.5 font-semibold">Harvests</th>
              <th class="px-3 py-2.5 font-semibold">Last</th>
              <th class="px-3 py-2.5 font-semibold">Next</th>
              <th class="px-3 py-2.5 font-semibold">Posts</th>
              <th class="px-3 py-2.5 pe-6 font-semibold">Followed by</th>
            </tr>
          </thead>
          <tbody class="@max-[43rem]/sections:block" role="rowgroup">
            <tr
              v-for="{ section, last } in activeSections"
              :key="section.pipelineId"
              class="border-b border-border @max-[43rem]/sections:grid @max-[43rem]/sections:grid-cols-[repeat(2,minmax(0,1fr))] @max-[43rem]/sections:gap-x-4 @max-[43rem]/sections:gap-y-3 @max-[43rem]/sections:px-4 @max-[43rem]/sections:py-4"
              role="row"
            >
              <th
                class="text-left font-semibold @max-[43rem]/sections:col-span-full @min-[43rem]/sections:px-6 @min-[43rem]/sections:py-3.5"
                scope="row"
                role="rowheader"
              >
                <a
                  v-if="webUrlOf(section.url)"
                  :href="webUrlOf(section.url)"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="inline-flex items-center gap-1 rounded hover:text-primary hover:underline"
                >
                  {{ session.sectionLabel(section.pipelineId) }}
                  <PillIcon name="external" />
                </a>
                <span v-else>{{ session.sectionLabel(section.pipelineId) }}</span>
              </th>
              <td class="@min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3.5" role="cell">
                <span aria-hidden="true" class="block text-xs text-muted-foreground @min-[43rem]/sections:hidden">
                  Harvests
                </span>
                {{ harvestWords(section.captureEvery) }}
                <span v-if="checkWords(section.checkEvery)" class="block text-[13px] text-muted-foreground">
                  {{ checkWords(section.checkEvery) }}
                </span>
              </td>
              <td class="@min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3.5" role="cell">
                <span aria-hidden="true" class="block text-xs text-muted-foreground @min-[43rem]/sections:hidden">
                  Last harvest
                </span>
                <template v-if="last">
                  <span class="inline-flex flex-wrap items-center gap-x-2 gap-y-0.5">
                    {{ clockOf(last.at) }}
                    <HarvestPill>
                      {{ statusWords(last.status) }}
                    </HarvestPill>
                  </span>
                  <span
                    v-if="last.status === 'completed' || last.status === 'partial'"
                    class="block text-[13px] tabular-nums text-muted-foreground"
                  >
                    {{ last.newPosts }} new · {{ last.found }} found
                  </span>
                  <span
                    v-if="reasonWords(last.reason, section.maxPages)"
                    class="block text-[13px] text-muted-foreground"
                  >
                    {{ reasonWords(last.reason, section.maxPages) }}
                  </span>
                </template>
                <span v-else class="text-muted-foreground">not yet</span>
              </td>
              <td class="@min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3.5" role="cell">
                <span aria-hidden="true" class="block text-xs text-muted-foreground @min-[43rem]/sections:hidden">
                  Next harvest
                </span>
                {{ section.nextDueAt ? clockOf(section.nextDueAt) : '—' }}
              </td>
              <td class="tabular-nums @min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3.5" role="cell">
                <span aria-hidden="true" class="block text-xs text-muted-foreground @min-[43rem]/sections:hidden">
                  Posts
                </span>
                {{ section.posts }}
              </td>
              <td
                class="@min-[43rem]/sections:px-3 @min-[43rem]/sections:py-3.5 @min-[43rem]/sections:pe-6"
                role="cell"
              >
                <span aria-hidden="true" class="block text-xs text-muted-foreground @min-[43rem]/sections:hidden">
                  Followed by
                </span>
                <ConsoleLink
                  v-if="section.subscribers && session.canOperate.value"
                  :to="{ kind: 'subscribers', section: section.pipelineId }"
                  class="font-medium text-primary underline decoration-primary/50 underline-offset-2 hover:decoration-primary"
                >
                  {{ plural(section.subscribers, 'subscriber') }}
                </ConsoleLink>
                <span v-else>{{ section.subscribers ? plural(section.subscribers, 'subscriber') : 'Nobody' }}</span>
              </td>
            </tr>
            <tr v-if="!harvested.length" class="border-b border-border">
              <td colspan="6" class="px-4 py-4 text-muted-foreground sm:px-6">
                No section is harvested yet. An operator turns one on in Settings.
              </td>
            </tr>
          </tbody>
        </table>
        <details v-if="others.length" class="group rounded-b-2xl bg-muted/40" v-bind="{ 'data-folded-sections': '' }">
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
            {{ others.length }} more {{ others.length === 1 ? 'section' : 'sections' }}
          </summary>
          <ul class="divide-y divide-border border-t border-border" aria-label="Sections not harvested">
            <li
              v-for="item in others"
              :key="item.key"
              class="flex items-center justify-between gap-3 px-6 py-2.5 text-sm"
            >
              <a
                v-if="item.url"
                :href="item.url"
                target="_blank"
                rel="noopener noreferrer"
                class="inline-flex items-center gap-1 rounded font-medium hover:text-primary hover:underline"
              >
                {{ item.label }}
                <PillIcon name="external" />
              </a>
              <span v-else class="font-medium">{{ item.label }}</span>
              <div class="flex flex-wrap items-center justify-end gap-x-3 text-end text-[13px] text-muted-foreground">
                <ConsoleLink
                  v-if="item.pipelineId && item.subscribers && session.canOperate.value"
                  :to="{ kind: 'subscribers', section: item.pipelineId }"
                  class="font-medium text-primary underline decoration-primary/50 underline-offset-2 hover:decoration-primary"
                >
                  {{ plural(item.subscribers, 'subscriber') }}
                </ConsoleLink>
                <span v-else-if="item.subscribers">{{ plural(item.subscribers, 'subscriber') }}</span>
                <span>{{ item.note }}</span>
              </div>
            </li>
          </ul>
        </details>
      </section>

      <div class="grid min-w-0 gap-5 @min-[43rem]/overview:grid-cols-2 @min-[67rem]/overview:grid-cols-1">
        <section class="rounded-2xl border border-border bg-card p-6" aria-label="Health">
          <h2 class="text-lg font-semibold">Health</h2>
          <dl class="mt-3 grid grid-cols-[minmax(0,1fr)_minmax(0,1fr)] gap-x-3 gap-y-2.5 text-sm sm:gap-x-6">
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
            <NewPostsBars :values="props.source.newPostsDaily" :height="96" :bar-width="8" fill show-readout />
          </div>
          <div class="mt-2 flex justify-between text-[13px] text-muted-foreground">
            <span>{{ trendStartOf(props.source.newPostsDaily.length || 14) }}</span>
            <span>today</span>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>
