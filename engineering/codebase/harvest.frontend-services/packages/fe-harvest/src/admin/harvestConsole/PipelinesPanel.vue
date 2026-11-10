<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { Button } from '@wow-two-beta/fe/presentation/actions';
import { Badge, Card, EmptyState } from '@wow-two-beta/fe/presentation/display';
import { PageHeaderActions } from '@wow-two-beta/fe/presentation/layout';
import type { PipelineModel } from '../../contracts';
import ConsoleLink from './ConsoleLink.vue';
import NewPipelineForm from './NewPipelineForm.vue';
import { runVariant, scheduleSummary, stateVariant } from './ConsoleFormat';
import { useHarvestConsole } from './HarvestConsole';
import { sameSection } from './SourceSections';

/**
 * Lists the open container's pipelines — each a standing harvest of one source section — and starts new ones. The
 * header action "New pipeline" opens the form; `newSource` opens it with that source chosen.
 */
defineOptions({ name: 'PipelinesPanel' });
const props = defineProps<{ newSource?: string }>();
const { pipelines, sectionsOf, sourceOf, runsOf, pipelineLabel, canWrite, navigation } = useHarvestConsole();
const creating = ref(!!props.newSource);
const canManage = computed(() => canWrite('pipelines:manage'));

watch(
  () => props.newSource,
  (value) => {
    if (value) creating.value = true;
  },
);

/** Names the section a pipeline follows: the adapter's label, else the saved URL, else the whole source. */
function sectionLabel(pipeline: PipelineModel) {
  const url = pipeline.scope.url;
  if (!url) return 'Whole source';
  return sectionsOf(pipeline.sourceId).find((item) => sameSection(url, item.url))?.label ?? url;
}

function created(pipeline: PipelineModel) {
  creating.value = false;
  navigation.value?.go({ kind: 'pipeline', id: pipeline.id });
}
</script>

<template>
  <div class="space-y-6">
    <PageHeaderActions v-if="canManage && !creating">
      <Button variant="outline" @click="creating = true">New pipeline</Button>
    </PageHeaderActions>
    <NewPipelineForm
      v-if="canManage && (creating || !pipelines.length)"
      :initial-source-id="props.newSource"
      @created="created"
      @cancel="creating = false"
    />
    <Card v-if="pipelines.length" class="p-0">
      <ul class="divide-y divide-border" aria-label="Pipelines">
        <li
          v-for="pipeline in pipelines"
          :key="pipeline.id"
          class="flex flex-wrap items-center gap-x-4 gap-y-2 px-6 py-4"
        >
          <span
            class="size-2 shrink-0 rounded-full"
            :class="pipeline.state === 'active' ? 'bg-primary/70' : 'bg-muted-foreground/50'"
            aria-hidden="true"
          ></span>
          <div class="min-w-0 flex-1">
            <ConsoleLink :to="{ kind: 'pipeline', id: pipeline.id }" class="font-medium hover:underline">
              {{ pipelineLabel(pipeline) }}
            </ConsoleLink>
            <p class="mt-1 truncate text-xs text-muted-foreground">
              {{ sourceOf(pipeline.sourceId)?.handle ?? 'Source' }} · {{ sectionLabel(pipeline) }} ·
              {{ scheduleSummary(pipeline) }}
            </p>
          </div>
          <Badge v-if="runsOf(pipeline.id)[0]" :variant="runVariant(runsOf(pipeline.id)[0]!)">
            last run {{ runsOf(pipeline.id)[0]!.status }}
          </Badge>
          <Badge :variant="stateVariant(pipeline.state)">{{ pipeline.state }}</Badge>
        </li>
      </ul>
    </Card>
    <Card v-else-if="!canManage" class="p-2">
      <EmptyState
        title="No pipelines yet"
        description="A pipeline follows one section of a source on its own schedule."
      />
    </Card>
  </div>
</template>
