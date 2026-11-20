<script setup lang="ts">
import { computed, onScopeDispose, shallowRef, watch } from 'vue';
import { AiBrandMark } from '@wow-two-beta/fe/domain/aiBrands';
import { MapLinks } from '@wow-two-beta/fe/domain/maps';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { EmptyState } from '@wow-two-beta/fe/presentation/display';
import type { ProcessedPassModel, SourceProcessedPostDetailsModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import ProcessedPostHeader from './ProcessedPostHeader.vue';
import { useHarvestConsole } from './HarvestConsole';
import {
  certaintyTone,
  costText,
  entityTypeWords,
  extractionRows,
  latencyText,
  markLabel,
  PassWords,
  shortRunId,
  tagFlagLabel,
  tokensDetail,
  VerdictWords,
  verdictTone,
} from './ProcessedWords';
import { agoOf, dateTimeOf, webUrlOf } from './SourceWords';

/**
 * One post as the AI passes read it, in full: the card's media header at a wider ratio, then the classification with
 * its verdict, reason and confidence; every tag; the extraction with each field's value, how sure the answer is, its
 * quotes from the post and its unsure flags; each AI pass with its model, cost, time, tokens, answer time and run; and
 * the links to the captured post and the original. A section the post has no data for does not show. The card in the
 * Processed tab opens this page.
 */
defineOptions({ name: 'ProcessedPostPanel' });
const props = defineProps<{ sourceId: string; postId: string }>();
const session = useHarvestConsole();
const source = computed(() => session.overviewOf(props.sourceId));
const headerSource = computed(() => source.value ?? { sourceId: props.sourceId, family: 'site' as const });
const details = shallowRef<SourceProcessedPostDetailsModel>();
const state = shallowRef<'loading' | 'ready' | 'unavailable'>('loading');
let read: AbortController | null = null;

async function load() {
  read?.abort();
  const controller = new AbortController();
  read = controller;
  state.value = 'loading';
  details.value = undefined;
  session.pageTitle.value = undefined;
  const result = await session.readSourceProcessedPost(props.sourceId, props.postId, controller.signal);
  if (controller.signal.aborted) return;
  state.value = result.ok ? 'ready' : 'unavailable';
  if (result.ok) {
    details.value = result.value;
    session.pageTitle.value = result.value.post.title;
  }
}
watch(() => [props.sourceId, props.postId], load, { immediate: true });
onScopeDispose(() => {
  read?.abort();
  session.pageTitle.value = undefined;
});

const post = computed(() => details.value?.post);
const tags = computed(() => post.value?.tags);
const classification = computed(() => post.value?.classification);
const extraction = computed(() => post.value?.extraction);
const primary = computed(() => tags.value?.domains.find((domain) => domain.primary));
const otherDomains = computed(() => tags.value?.domains.filter((domain) => !domain.primary) ?? []);
const extracted = computed(() =>
  extraction.value ? extractionRows(extraction.value, details.value?.evidence ?? []) : undefined,
);
const extractionLabel = computed(() =>
  extraction.value?.useCase === 'extract-listing' ? 'Listing extraction' : 'Event extraction',
);
const original = computed(() => (post.value?.state === 'gone' ? undefined : webUrlOf(post.value?.url)));

/** One pass's figures, each with the words that label it and, where it helps, the exact value on hover. */
function figuresOf(pass: ProcessedPassModel) {
  const { answer } = pass;
  return [
    { label: 'Cost', value: costText(answer.costMicroUsd) },
    { label: 'Time', value: latencyText(answer.latencyMs) },
    { label: 'Tokens', value: tokensDetail(answer) },
    { label: 'Answered', value: agoOf(answer.answeredAt), title: dateTimeOf(answer.answeredAt) },
    {
      label: 'Prompt',
      value:
        answer.promptVersion !== undefined && answer.promptVersion !== null ? `p${answer.promptVersion}` : undefined,
    },
    { label: 'Run', value: shortRunId(pass.inferenceId), title: pass.inferenceId },
  ].filter((figure): figure is { label: string; value: string; title?: string } => !!figure.value);
}

const SectionClass = 'min-w-0 rounded-2xl border border-border bg-card p-5';
const LinkClass =
  'inline-flex h-9 items-center gap-1.5 rounded-lg border border-border bg-card px-3 text-sm font-medium hover:bg-muted focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring';
</script>

<template>
  <div class="flex min-w-0 flex-col gap-5" role="region" aria-label="Processed post">
    <ConsoleLink
      :to="{ kind: 'source', id: props.sourceId, tab: 'processed' }"
      class="inline-flex w-fit items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
    >
      <svg
        width="14"
        height="14"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2.2"
        aria-hidden="true"
      >
        <path d="M15 6l-6 6 6 6" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
      Processed posts of {{ source?.name ?? 'the source' }}
    </ConsoleLink>

    <p v-if="state === 'loading'" class="text-sm text-muted-foreground" role="status">Reading the processed post…</p>
    <div v-else-if="state === 'unavailable'" class="rounded-2xl border border-border bg-card p-2">
      <EmptyState
        title="Could not read this processed post"
        description="It may not be processed yet, or the console could not reach SCFD."
      >
        <template #actions>
          <Button variant="outline" @click="load">Retry</Button>
          <ConsoleLink
            :to="{ kind: 'source', id: props.sourceId, tab: 'posts', post: props.postId }"
            class="text-sm text-primary hover:underline"
          >
            Open the post
          </ConsoleLink>
        </template>
      </EmptyState>
    </div>
    <template v-else-if="post && tags">
      <div class="overflow-hidden rounded-2xl border border-border bg-card" aria-label="Post header" role="group">
        <ProcessedPostHeader :source="headerSource" :post="post" wide />
      </div>

      <div class="grid gap-5 lg:grid-cols-[repeat(auto-fit,minmax(min(100%,24rem),1fr))]">
        <section v-if="classification" aria-label="Classification" :class="SectionClass">
          <h2 class="text-[15px] font-semibold">Classification</h2>
          <dl class="mt-3 grid grid-cols-[7rem_minmax(0,1fr)] items-start gap-x-4 gap-y-2.5 text-[15px]">
            <dt class="text-muted-foreground">Verdict</dt>
            <dd>
              <HarvestPill :tone="verdictTone(classification.verdict)">
                {{ VerdictWords[classification.verdict] }}
              </HarvestPill>
            </dd>
            <template v-if="classification.reason">
              <dt class="text-muted-foreground">Reason</dt>
              <dd class="break-words">{{ classification.reason }}</dd>
            </template>
            <template v-if="typeof classification.confidence === 'number'">
              <dt class="text-muted-foreground">Confidence</dt>
              <dd>{{ Math.round(classification.confidence * 100) }}%</dd>
            </template>
            <template v-if="classification.kind">
              <dt class="text-muted-foreground">Kind</dt>
              <dd>{{ classification.kind }}</dd>
            </template>
            <dt class="text-muted-foreground">Classifier</dt>
            <dd class="font-mono text-[13px] leading-5 break-all">{{ classification.classifier }}</dd>
          </dl>
        </section>

        <section aria-label="Tags" :class="SectionClass">
          <h2 class="text-[15px] font-semibold">Tags</h2>
          <p v-if="tags.status !== 'done'" class="mt-3 text-sm text-destructive" role="status">
            Tagging {{ tags.status }}
            <template v-if="tags.error">· {{ tags.error }}</template>
          </p>
          <template v-else>
            <dl class="mt-3 grid grid-cols-[7rem_minmax(0,1fr)] items-start gap-x-4 gap-y-2.5 text-[15px]">
              <dt class="text-muted-foreground">Entity type</dt>
              <dd>
                <HarvestPill tone="info">{{ entityTypeWords(tags.entityType) }}</HarvestPill>
              </dd>
              <template v-if="primary">
                <dt class="text-muted-foreground">Primary domain</dt>
                <dd class="flex min-w-0 flex-wrap items-center gap-1.5">
                  <HarvestPill tone="outline" shrinkable>{{ primary.label }}</HarvestPill>
                  <span class="font-mono text-xs break-all text-muted-foreground">{{ primary.key }}</span>
                </dd>
              </template>
              <template v-if="otherDomains.length">
                <dt class="text-muted-foreground">Other domains</dt>
                <dd>
                  <ul class="flex min-w-0 flex-wrap gap-1.5" aria-label="Other domains">
                    <li v-for="domain in otherDomains" :key="domain.key">
                      <HarvestPill shrinkable v-bind="{ title: domain.key }">{{ domain.label }}</HarvestPill>
                    </li>
                  </ul>
                </dd>
              </template>
              <template v-if="tags.topics.length">
                <dt class="text-muted-foreground">Topics</dt>
                <dd>
                  <ul class="flex min-w-0 flex-wrap gap-1.5" aria-label="Topics">
                    <li v-for="topic in tags.topics" :key="topic">
                      <HarvestPill tone="dashed" shrinkable>#{{ topic }}</HarvestPill>
                    </li>
                  </ul>
                </dd>
              </template>
              <template v-if="tags.timeframe">
                <dt class="text-muted-foreground">Timeframe</dt>
                <dd>
                  <HarvestPill tone="dashed">{{ tags.timeframe }}</HarvestPill>
                </dd>
              </template>
              <template v-if="typeof tags.confidence === 'number'">
                <dt class="text-muted-foreground">Confidence</dt>
                <dd>{{ Math.round(tags.confidence * 100) }}%</dd>
              </template>
              <template v-if="tags.about">
                <dt class="text-muted-foreground">About</dt>
                <dd class="break-words">{{ tags.about }}</dd>
              </template>
            </dl>
            <p v-if="!tags.current" class="mt-3">
              <HarvestPill tone="warning">tags from an earlier text</HarvestPill>
            </p>
            <ul v-if="tags.flags.length" class="mt-3 grid gap-1.5" aria-label="Unsure tags">
              <li v-for="flag in tags.flags" :key="flag.field" class="flex flex-wrap items-center gap-2 text-[13px]">
                <HarvestPill tone="warning">? {{ tagFlagLabel(flag.field) }}</HarvestPill>
                <span class="min-w-0 break-words">
                  {{ flag.reason }}
                  <template v-if="flag.proposed">→ {{ flag.proposed }}</template>
                </span>
              </li>
            </ul>
          </template>
        </section>
      </div>

      <section v-if="extraction && extracted" :aria-label="extractionLabel" :class="SectionClass">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="text-[15px] font-semibold">Extraction</h2>
          <HarvestPill v-if="extraction.role" tone="neutral" v-bind="{ 'data-role': '' }">
            {{ extraction.role }}
          </HarvestPill>
          <template v-if="extraction.status !== 'done'">
            <HarvestPill tone="warning" v-bind="{ 'data-status': '' }">{{ extraction.status }}</HarvestPill>
            <span v-if="extraction.error" class="text-sm text-muted-foreground">{{ extraction.error }}</span>
          </template>
        </div>
        <ul v-if="extracted.rows.length" class="mt-2 divide-y divide-border" aria-label="Extracted fields">
          <li
            v-for="row in extracted.rows"
            :key="row.key"
            class="grid gap-1 py-3 sm:grid-cols-[10rem_minmax(0,1fr)] sm:gap-x-4"
            v-bind="{ 'data-field': row.key }"
          >
            <span class="text-sm text-muted-foreground">{{ row.label }}</span>
            <div class="grid min-w-0 gap-1.5">
              <p class="flex flex-wrap items-center gap-2 text-[15px] font-semibold">
                <span class="min-w-0 break-words">{{ row.value }}</span>
                <MapLinks v-if="row.key === 'place' || row.key === 'location'" :query="row.value" />
                <HarvestPill
                  v-if="row.certainty"
                  :tone="certaintyTone(row.certainty)"
                  v-bind="{ 'data-certainty': '' }"
                >
                  {{ row.certainty }}
                </HarvestPill>
              </p>
              <p
                v-for="flag in row.flags"
                :key="flag.field"
                class="flex flex-wrap items-center gap-2 text-[13px]"
                v-bind="{ 'data-flag': flag.field }"
              >
                <HarvestPill tone="warning">? unsure</HarvestPill>
                <span class="min-w-0 break-words">
                  {{ flag.reason }}
                  <template v-if="flag.proposed">→ {{ flag.proposed }}</template>
                </span>
              </p>
              <blockquote
                v-for="quote in row.quotes"
                :key="quote"
                class="border-s-2 border-border ps-3 text-[13px] leading-5 break-words text-muted-foreground italic"
                v-bind="{ 'data-evidence': '' }"
              >
                “{{ quote }}”
              </blockquote>
            </div>
          </li>
        </ul>
        <ul v-if="extracted.strayFlags.length" class="mt-3 grid gap-1.5" aria-label="Other unsure fields">
          <li
            v-for="flag in extracted.strayFlags"
            :key="flag.field"
            class="flex flex-wrap items-center gap-2 text-[13px]"
          >
            <HarvestPill tone="warning">? {{ flag.field }}</HarvestPill>
            <span class="min-w-0 break-words">
              {{ flag.reason }}
              <template v-if="flag.proposed">→ {{ flag.proposed }}</template>
            </span>
          </li>
        </ul>
      </section>

      <section v-if="details?.passes.length" aria-label="AI passes" :class="SectionClass">
        <h2 class="text-[15px] font-semibold">AI passes</h2>
        <ul class="mt-2 divide-y divide-border">
          <li
            v-for="pass in details.passes"
            :key="pass.pass"
            class="grid gap-2 py-3"
            v-bind="{ 'data-pass': pass.pass }"
          >
            <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1">
              <span
                class="inline-grid size-7 shrink-0 place-items-center rounded-full border border-border bg-card"
                v-bind="{ 'data-pass-brand': '' }"
              >
                <AiBrandMark :provider="pass.answer.provider" :model="pass.answer.model" :size="16" />
              </span>
              <span class="text-[15px] font-semibold">{{ PassWords[pass.pass] ?? pass.pass }}</span>
              <span class="text-sm text-muted-foreground">
                {{ markLabel(pass.answer.provider, pass.answer.model) }}
              </span>
              <template v-if="pass.status !== 'done'">
                <HarvestPill tone="warning">{{ pass.status }}</HarvestPill>
                <span v-if="pass.error" class="text-sm text-muted-foreground">{{ pass.error }}</span>
              </template>
            </div>
            <dl class="flex flex-wrap gap-x-6 gap-y-1 ps-9 text-[13px]">
              <div v-for="figure in figuresOf(pass)" :key="figure.label" class="flex gap-1.5">
                <dt class="text-muted-foreground">{{ figure.label }}</dt>
                <dd class="font-semibold" :title="figure.title">{{ figure.value }}</dd>
              </div>
            </dl>
          </li>
        </ul>
      </section>

      <section aria-label="Links" :class="SectionClass">
        <h2 class="text-[15px] font-semibold">Links</h2>
        <div class="mt-3 flex flex-wrap items-center gap-3">
          <ConsoleLink
            :to="{ kind: 'source', id: props.sourceId, tab: 'posts', post: props.postId }"
            :class="LinkClass"
          >
            Open post
          </ConsoleLink>
          <a v-if="original" :href="original" target="_blank" rel="noopener noreferrer" :class="LinkClass">
            Open original
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2.2"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
            >
              <path d="M7 17 17 7M8 7h9v9" />
            </svg>
          </a>
        </div>
      </section>
    </template>
  </div>
</template>
