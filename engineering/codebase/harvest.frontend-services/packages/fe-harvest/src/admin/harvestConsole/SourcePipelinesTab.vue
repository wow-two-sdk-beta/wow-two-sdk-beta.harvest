<script setup lang="ts">
import { computed } from 'vue';
import type { SourceOverviewModel } from '../../contracts';
import ChoicePicker from './ChoicePicker.vue';
import { useHarvestConsole } from './HarvestConsole';
import { usePipelineStages } from './UsePipelineStages';
import PipelineStageCard from './PipelineStageCard.vue';
import SourceProfilePipelines from './SourceProfilePipelines.vue';
import { sourceProfileSchema } from './SourceProfileSchema';

const props = defineProps<{ source: SourceOverviewModel }>();
const session = useHarvestConsole();
const profileSchema = computed(() =>
  sourceProfileSchema(session.capabilities.value[props.source.sourceId]?.configuration.scopeSchema),
);
const sections = computed(() => props.source.sections);
const choices = computed(() =>
  sections.value.map(({ pipelineId }) => ({ value: pipelineId, label: session.sectionLabel(pipelineId) })),
);
const pipelineId = computed(() =>
  profileSchema.value?.fixedUrl
    ? ''
    : sections.value.some((item) => item.pipelineId === session.focusedPipelineId.value)
      ? session.focusedPipelineId.value
      : (sections.value[0]?.pipelineId ?? ''),
);
const { client, stages, loading, error, refresh } = usePipelineStages(pipelineId);
</script>

<template>
  <SourceProfilePipelines v-if="profileSchema?.fixedUrl" :source="source" :schema="profileSchema" />
  <div v-else class="grid min-w-0 gap-4">
    <p v-if="!client" class="text-sm text-muted-foreground">Source stages are unavailable for this connection.</p>
    <p v-else-if="!sections.length" class="text-sm text-muted-foreground">
      Add a source section to configure its stages.
    </p>
    <template v-else>
      <div v-if="sections.length > 1" class="grid w-full min-w-0 gap-1 text-sm sm:w-72">
        <span>Section</span>
        <ChoicePicker
          :model-value="pipelineId"
          :choices="choices"
          label="Section"
          @update:model-value="session.focusedPipelineId.value = $event"
        />
      </div>
      <p v-if="error" class="text-sm text-destructive" role="alert">Stages could not be loaded.</p>
      <p v-else-if="loading" class="text-sm text-muted-foreground" role="status">Loading stages…</p>
      <p v-else-if="!stages.length" class="text-sm text-muted-foreground">No stages are configured.</p>
      <PipelineStageCard
        v-for="stage in stages"
        :key="`${pipelineId}:${stage.kind}`"
        :stage="stage"
        :pipeline-id="pipelineId"
        @changed="refresh()"
      />
    </template>
  </div>
</template>
