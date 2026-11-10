<script setup lang="ts">
import { computed } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, Card, EmptyState } from '@wow-two-beta/fe/presentation/display';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import type { PipelineUpdate } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import PipelineSettings from './PipelineSettings.vue';
import RunList from './RunList.vue';
import SourceCapabilitiesPanel from './SourceCapabilitiesPanel.vue';
import WorkerOffNotice from './WorkerOffNotice.vue';
import { formatDate, scheduleSummary, stateVariant } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';
import { sameSection } from './SourceSections';

/**
 * One pipeline's page in three parts — its configuration, its schedule and its runs — under an overview of the source
 * and section it follows. The header actions run it now and pause or resume it.
 */
defineOptions({ name: 'PipelinePanel' });
const props = defineProps<{ pipelineId: string }>();
const session = useHarvestConsole();
const { loaded, pending, capabilities, capabilityErrors, localWorkerEnabled, canWrite, canCapture } = session;

const pipeline = computed(() => session.pipelineOf(props.pipelineId));
const source = computed(() => (pipeline.value ? session.sourceOf(pipeline.value.sourceId) : undefined));
const metadata = computed(() => (pipeline.value ? capabilities.value[pipeline.value.sourceId] : undefined));
const runs = computed(() => session.runsOf(props.pipelineId));
const section = computed(() => {
  const url = pipeline.value?.scope.url;
  if (!pipeline.value || !url) return 'Whole source';
  return session.sectionsOf(pipeline.value.sourceId).find((item) => sameSection(url, item.url))?.label ?? url;
});

function save(update: PipelineUpdate) {
  void session.savePipeline(props.pipelineId, update);
}
</script>

<template>
  <div v-if="pipeline" class="space-y-6">
    <PageHeaderActions>
      <Button
        v-if="canWrite('pipelines:manage')"
        variant="outline"
        :is-disabled="pending"
        @click="session.setPipelinePaused(pipeline.id, pipeline.state !== 'paused')"
      >
        {{ pipeline.state === 'paused' ? 'Resume' : 'Pause' }}
      </Button>
      <Button
        v-if="canWrite('pipelines:run')"
        :is-disabled="pending || pipeline.state !== 'active' || !canCapture(pipeline.sourceId)"
        @click="session.startRun(pipeline.id)"
      >
        {{ localWorkerEnabled === false ? 'Queue capture' : 'Run now' }}
      </Button>
    </PageHeaderActions>

    <Card class="p-6">
      <dl class="grid gap-x-6 gap-y-4 text-sm sm:grid-cols-2 xl:grid-cols-4" aria-label="Pipeline overview">
        <div>
          <dt class="text-xs text-muted-foreground">State</dt>
          <dd class="mt-1">
            <Badge :variant="stateVariant(pipeline.state)">{{ pipeline.state }}</Badge>
          </dd>
        </div>
        <div class="min-w-0">
          <dt class="text-xs text-muted-foreground">Source</dt>
          <dd class="mt-1 truncate">
            <ConsoleLink :to="{ kind: 'sources' }" class="hover:underline">
              {{ source?.handle ?? 'Source' }}
            </ConsoleLink>
          </dd>
        </div>
        <div class="min-w-0">
          <dt class="text-xs text-muted-foreground">Section</dt>
          <dd class="mt-1 truncate" :title="pipeline.scope.url">{{ section }}</dd>
        </div>
        <div class="min-w-0">
          <dt class="text-xs text-muted-foreground">Cadence</dt>
          <dd class="mt-1 truncate">{{ scheduleSummary(pipeline) }}</dd>
        </div>
      </dl>
      <SourceCapabilitiesPanel class="mt-5" :capabilities="metadata" :error="capabilityErrors[pipeline.sourceId]" />
    </Card>
    <WorkerOffNotice />

    <PipelineSettings
      v-if="canWrite('pipelines:manage')"
      :key="pipeline.id"
      :pipeline="pipeline"
      :pending="pending"
      :capabilities="metadata"
      @save="save"
    />
    <div v-else class="grid gap-6 lg:grid-cols-2">
      <section aria-label="Configuration">
        <Card class="p-6">
          <h2 class="text-base font-semibold">Configuration</h2>
          <dl class="mt-4 space-y-3 text-sm">
            <div>
              <dt class="text-xs text-muted-foreground">Starting URL</dt>
              <dd class="mt-1 break-all">{{ pipeline.scope.url ?? 'Whole source' }}</dd>
            </div>
            <div v-for="(value, key) in pipeline.scope.parameters" :key="key">
              <dt class="text-xs text-muted-foreground">{{ key }}</dt>
              <dd class="mt-1 break-all">{{ value }}</dd>
            </div>
          </dl>
        </Card>
      </section>
      <section aria-label="Schedule">
        <Card class="p-6">
          <h2 class="text-base font-semibold">Schedule</h2>
          <ul class="mt-4 space-y-2 text-sm">
            <li v-for="schedule in pipeline.schedules" :key="schedule.kind">
              <span class="font-medium capitalize">{{ schedule.kind }}</span>
              <span class="ml-2 text-muted-foreground">
                {{
                  schedule.enabled && schedule.mode !== 'manual'
                    ? (schedule.cron ?? schedule.every ?? schedule.mode)
                    : 'Manual only'
                }}
                <template v-if="schedule.nextDueAtUtc">· next {{ formatDate(schedule.nextDueAtUtc) }}</template>
              </span>
            </li>
          </ul>
          <p v-if="!pipeline.schedules.length" class="mt-4 text-sm text-muted-foreground">Manual runs only.</p>
        </Card>
      </section>
    </div>

    <section aria-label="Pipeline runs">
      <Card class="p-6">
        <div class="mb-5 flex items-center justify-between gap-3">
          <h2 class="text-base font-semibold">Runs</h2>
          <ConsoleLink :to="{ kind: 'runs', pipeline: pipeline.id }" class="text-sm text-primary hover:underline">
            Open in Runs
          </ConsoleLink>
        </div>
        <RunList :runs="runs" />
      </Card>
    </section>
  </div>
  <p v-else-if="!loaded" class="text-sm text-muted-foreground" role="status">Loading the pipeline…</p>
  <Card v-else class="p-2">
    <EmptyState title="Pipeline not found" description="This pipeline is not in the open container.">
      <template #actions>
        <ConsoleLink :to="{ kind: 'pipelines' }" class="text-sm text-primary hover:underline">
          All pipelines
        </ConsoleLink>
      </template>
    </EmptyState>
  </Card>
</template>
