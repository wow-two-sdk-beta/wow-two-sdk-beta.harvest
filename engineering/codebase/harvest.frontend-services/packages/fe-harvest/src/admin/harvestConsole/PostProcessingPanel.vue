<script setup lang="ts">
import { onScopeDispose, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import ConsoleLink from './ConsoleLink.vue';
import HarvestPill from './HarvestPill.vue';
import { useHarvestConsole } from './HarvestConsole';
import { dateTimeOf } from './SourceWords';
import type { PostProcessingModel } from '../../contracts';

/** Displays processor-owned fields and their evidence without presenting a consumer listing. */
defineOptions({ name: 'PostProcessingPanel' });
const props = defineProps<{ sourceId: string; postId: string }>();
const session = useHarvestConsole();
const results = ref<readonly PostProcessingModel[]>([]);
const state = ref<'loading' | 'ready' | 'unavailable'>('loading');
let read: AbortController | undefined;

async function refresh() {
  read?.abort();
  const controller = new AbortController();
  read = controller;
  state.value = 'loading';
  results.value = [];
  const result = await session.readPostProcessing(props.sourceId, props.postId, controller.signal);
  if (controller.signal.aborted) return;
  state.value = result.ok ? 'ready' : 'unavailable';
  if (result.ok) results.value = result.value.filter((item) => item.sourceId === props.sourceId);
}
watch(() => [props.postId, props.sourceId], refresh, { immediate: true });
onScopeDispose(() => read?.abort());

function record(value: unknown): Record<string, unknown> | undefined {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}
interface Field {
  key: string;
  label: string;
  status: string;
  claims: { evidence: string; value: Record<string, unknown> | undefined }[];
}
function fieldsOf(output: unknown): Field[] {
  const fields = record(output)?.fields;
  if (!Array.isArray(fields)) return [];
  return fields.flatMap((item): Field[] => {
    const field = record(item);
    if (
      !field ||
      typeof field.key !== 'string' ||
      typeof field.label !== 'string' ||
      typeof field.status !== 'string' ||
      !Array.isArray(field.claims)
    )
      return [];
    return [
      {
        key: field.key,
        label: field.label,
        status: field.status,
        claims: field.claims.flatMap((item) => {
          const claim = record(item);
          return claim && typeof claim.evidence === 'string'
            ? [{ evidence: claim.evidence, value: record(claim.value) }]
            : [];
        }),
      },
    ];
  });
}
function statusOf(status: string): string {
  const labels: Record<string, string> = {
    unknown: 'Not determined',
    absent: 'Explicitly absent',
    present: 'Stated',
    conflict: 'Conflicting statements',
  };
  return labels[status] ?? status;
}
function words(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('_', ' ');
}
function valuesOf(value: Record<string, unknown> | undefined, prefix = '', depth = 0): [string, string][] {
  return Object.entries(value ?? {}).flatMap(([key, item]): [string, string][] => {
    const label = prefix + words(key);
    if (typeof item === 'string' || typeof item === 'number' || typeof item === 'boolean')
      return [[label, String(item)]];
    return depth < 3 && record(item) ? valuesOf(record(item), label + ' · ', depth + 1) : [];
  });
}
function jsonOf(output: unknown): string {
  return JSON.stringify(output, null, 2);
}
</script>

<template>
  <section aria-label="Processing results" class="min-w-0 rounded-2xl border border-border bg-card p-5">
    <div class="flex items-center justify-between gap-3">
      <div>
        <h2 class="text-[15px] font-semibold">Processing results</h2>
        <p class="mt-1 text-sm text-muted-foreground">Derived fields with the captured statements that support them.</p>
      </div>
      <Button size="sm" variant="outline" :is-disabled="state === 'loading'" @click="refresh">Refresh</Button>
    </div>
    <p v-if="state === 'loading'" role="status" class="mt-4 text-sm text-muted-foreground">Reading results…</p>
    <p v-else-if="state === 'unavailable'" role="alert" class="mt-4 text-sm text-muted-foreground">
      Processing results could not be read. Refresh to try again.
    </p>
    <p v-else-if="!results.length" class="mt-4 text-sm text-muted-foreground">
      No processing results for this source yet.
    </p>
    <div v-else class="mt-4 flex flex-col gap-4">
      <article
        v-for="result in results"
        :key="result.captureId + ':' + result.processor"
        class="min-w-0 rounded-xl border border-border p-4"
      >
        <div class="flex flex-wrap items-center justify-between gap-3">
          <div class="flex min-w-0 flex-wrap items-center gap-2">
            <h3 class="break-all text-sm font-semibold">{{ result.processor }}</h3>
            <HarvestPill tone="outline">{{ result.isCurrent ? 'Current capture' : 'Older capture' }}</HarvestPill>
            <HarvestPill tone="outline">{{ result.output == null ? result.runStatus : 'Processed' }}</HarvestPill>
          </div>
          <ConsoleLink
            :to="{ kind: 'source', id: sourceId, tab: 'activity', run: result.runId }"
            class="text-sm font-medium text-primary hover:underline"
          >
            View run
          </ConsoleLink>
        </div>
        <p v-if="result.processedAtUtc" class="mt-2 text-xs text-muted-foreground">
          {{ dateTimeOf(result.processedAtUtc) }}
        </p>
        <p v-if="result.output == null" class="mt-3 text-sm text-muted-foreground">
          {{
            result.requiresReview
              ? 'Automatic retries stopped. Review the run before processing a new capture or prompt version.'
              : 'No completed output was saved for this attempt. See the run for its progress or error.'
          }}
        </p>
        <template v-else>
          <div v-if="fieldsOf(result.output).length" class="mt-4 grid gap-3 md:grid-cols-2">
            <section
              v-for="field in fieldsOf(result.output)"
              :key="field.key"
              :aria-label="field.label"
              class="min-w-0 rounded-lg border border-border p-3"
            >
              <div class="flex flex-wrap items-center justify-between gap-2">
                <h4 class="text-sm font-semibold">{{ field.label }}</h4>
                <span class="text-xs text-muted-foreground">{{ statusOf(field.status) }}</span>
              </div>
              <div v-for="(claim, index) in field.claims" :key="index" class="mt-3">
                <dl v-if="valuesOf(claim.value).length" class="mb-2 flex flex-wrap gap-x-4 gap-y-1 text-sm">
                  <div v-for="[key, value] in valuesOf(claim.value)" :key="key" class="flex gap-1">
                    <dt class="capitalize text-muted-foreground">{{ key }}:</dt>
                    <dd>{{ value }}</dd>
                  </div>
                </dl>
                <blockquote class="whitespace-pre-wrap break-words border-l-2 border-primary/40 pl-3 text-sm">
                  {{ claim.evidence }}
                </blockquote>
              </div>
            </section>
          </div>
          <details class="mt-4 text-sm">
            <summary class="w-fit cursor-pointer text-muted-foreground">Output and inference metadata</summary>
            <pre class="mt-3 max-h-96 overflow-auto whitespace-pre-wrap break-all rounded-lg bg-muted/30 p-3 text-xs">{{
              jsonOf(result.output)
            }}</pre>
          </details>
        </template>
        <details class="mt-3 text-xs text-muted-foreground">
          <summary class="w-fit cursor-pointer">Capture identifiers</summary>
          <dl class="mt-2 grid gap-1">
            <div>
              <dt class="inline">Capture:</dt>
              <dd class="inline break-all">{{ result.captureId }}</dd>
            </div>
            <div>
              <dt class="inline">Link:</dt>
              <dd class="inline break-all">{{ result.linkId }}</dd>
            </div>
          </dl>
        </details>
      </article>
      <p v-if="results.length === 50" class="text-xs text-muted-foreground">Showing the latest 50 results.</p>
    </div>
  </section>
</template>
