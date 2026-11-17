<script setup lang="ts">
import { computed } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { SwitchInput } from '@wow-two-beta/fe/presentation/forms';
import type { PipelineModel, SourceOverviewModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import { useHarvestConsole } from './HarvestConsole';
import { activeRun } from './PipelineActivity';
import SourceProfileForm from './SourceProfileForm.vue';
import type { SourceProfileSchema } from './SourceProfileSchema';
import { sameSection } from './SourceSections';

/** Runs and bounds the current source's saved publisher pipelines without creating schedules. */
defineOptions({ name: 'SourceProfilePipelines' });
const props = defineProps<{ source: SourceOverviewModel; schema: SourceProfileSchema }>();
const session = useHarvestConsole();
const { pending } = session;
const pipelines = computed(() =>
  session.pipelines.value.filter((pipeline) => pipeline.sourceId === props.source.sourceId),
);
const canManage = computed(() => session.canOperate.value && session.canWrite('pipelines:manage'));
const canCapture = computed(() => session.canCapture(props.source.sourceId));
const sourceState = computed(() => props.source.sourceState ?? session.sourceOf(props.source.sourceId)?.state);

function canCaptureProfile(pipeline: PipelineModel) {
  const permission = props.source.permission ?? session.sourceOf(props.source.sourceId)?.permission;
  return (
    !pending.value &&
    session.canWrite('pipelines:run') &&
    canCapture.value &&
    sourceState.value === 'active' &&
    (!permission || permission === 'open' || permission === 'granted') &&
    pipeline.state === 'active' &&
    pipeline.sourceId === props.source.sourceId &&
    !!pipeline.scope.url &&
    (!props.schema.fixedUrl || sameSection(pipeline.scope.url, props.schema.fixedUrl)) &&
    session.localWorkerEnabled.value !== false &&
    !activeRun(session.runsOf(pipeline.id))
  );
}
function captureProfile(pipeline: PipelineModel) {
  if (canCaptureProfile(pipeline)) void session.startRun(pipeline.id, 'capture');
}
</script>

<template>
  <div class="grid min-w-0 gap-5">
    <p v-if="schema.fixedUrl && (pipelines.length || !canManage)" class="text-sm text-muted-foreground wrap-anywhere">
      {{ schema.fixedUrl }}
    </p>
    <p v-if="!canCapture" class="text-sm text-muted-foreground" role="status">
      This source’s reader cannot harvest on this server yet.
    </p>
    <SourceProfileForm
      v-if="canManage && (!schema.fixedUrl || !pipelines.length)"
      :source-id="source.sourceId"
      :schema="schema"
    />
    <article
      v-for="pipeline in pipelines"
      :key="pipeline.id"
      class="grid min-w-0 gap-3 rounded-xl border border-border bg-card p-4"
    >
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h3 class="min-w-0 wrap-anywhere text-sm font-semibold">{{ pipeline.name || source.name }}</h3>
        <div class="flex flex-wrap items-center gap-2 text-sm">
          <Button
            v-if="session.canWrite('pipelines:run')"
            variant="outline"
            :is-disabled="!canCaptureProfile(pipeline)"
            :aria-label="`Capture ${pipeline.name || source.name} now`"
            @click="captureProfile(pipeline)"
          >
            Capture now
          </Button>
          <template v-if="canManage">
            <span>Enabled</span>
            <SwitchInput
              :model-value="pipeline.state === 'active'"
              :is-disabled="pending || !['active', 'paused'].includes(pipeline.state)"
              v-bind="{ 'aria-label': `Enable ${pipeline.name || source.name}` }"
              @update:model-value="(on: boolean) => session.setPipelinePaused(pipeline.id, !on)"
            />
          </template>
        </div>
      </div>
      <SourceProfileForm v-if="canManage" :source-id="source.sourceId" :schema="schema" :pipeline="pipeline" />
      <p class="text-xs text-muted-foreground">Capture now runs once using these saved limits.</p>
      <ConsoleLink
        :to="{ kind: 'pipeline', id: pipeline.id, tab: 'logs' }"
        class="text-sm text-primary underline-offset-2 hover:underline"
      >
        View run history
      </ConsoleLink>
    </article>
  </div>
</template>
