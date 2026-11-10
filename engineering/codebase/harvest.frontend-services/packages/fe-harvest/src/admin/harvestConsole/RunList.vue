<script setup lang="ts">
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge } from '@wow-two-beta/fe/presentation/display';
import type { RunModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import { formatDate, isActiveRun, runVariant } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';

/** Lists runs with their outcome and counters; a queued or running one can be cancelled. */
defineOptions({ name: 'RunList' });
const props = withDefaults(
  defineProps<{
    runs: readonly RunModel[];
    /** Names each run's pipeline, for lists that span pipelines. */
    showPipeline?: boolean;
    emptyText?: string;
  }>(),
  { showPipeline: false, emptyText: 'No runs yet. Start a pipeline to see its progress.' },
);
const { canWrite, pending, cancelRun, pipelineOf, pipelineLabel } = useHarvestConsole();

function pipelineName(run: RunModel) {
  const pipeline = pipelineOf(run.pipelineId);
  return pipeline ? pipelineLabel(pipeline) : 'Pipeline';
}
</script>

<template>
  <ul v-if="props.runs.length" class="divide-y divide-border" aria-label="Runs">
    <li v-for="run in props.runs" :key="run.id" class="space-y-2 py-4 first:pt-0 last:pb-0">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
          <Badge :variant="runVariant(run)">{{ run.status }}</Badge>
          <span class="text-sm font-medium capitalize">{{ run.kind }}</span>
          <ConsoleLink
            v-if="props.showPipeline"
            :to="{ kind: 'pipeline', id: run.pipelineId }"
            class="min-w-0 truncate text-sm text-primary hover:underline"
          >
            {{ pipelineName(run) }}
          </ConsoleLink>
          <span class="text-xs text-muted-foreground">{{ formatDate(run.queuedAtUtc) }}</span>
        </div>
        <Button
          v-if="canWrite('pipelines:run') && isActiveRun(run)"
          size="sm"
          variant="outline"
          :is-disabled="pending"
          @click="cancelRun(run.id)"
        >
          Cancel
        </Button>
      </div>
      <p class="text-sm text-muted-foreground">
        {{ run.captured }} captured · {{ run.unchanged }} unchanged · {{ run.discovered }} discovered ·
        {{ run.failed }} failed
      </p>
      <p v-if="run.fetchOutcome || run.message" class="text-sm">{{ run.fetchOutcome }} {{ run.message }}</p>
    </li>
  </ul>
  <p v-else class="text-sm text-muted-foreground">{{ props.emptyText }}</p>
</template>
