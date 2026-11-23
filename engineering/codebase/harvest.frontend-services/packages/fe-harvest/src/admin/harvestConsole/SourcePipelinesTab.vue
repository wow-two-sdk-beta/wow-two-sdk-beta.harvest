<script setup lang="ts">
import { computed } from 'vue';
import type { ModelProcessingMode, SourceOverviewModel } from '../../contracts';
import ChoicePicker from './ChoicePicker.vue';
import ConsoleLink from './ConsoleLink.vue';
import { useHarvestConsole } from './HarvestConsole';
import { usePipelineStages } from './UsePipelineStages';
import PipelineStageCard from './PipelineStageCard.vue';
import SourceProfilePipelines from './SourceProfilePipelines.vue';
import { sourceProfileSchema } from './SourceProfileSchema';
import { ModelProcessingChoices, useSourceModelProcessing } from './UseSourceModelProcessing';

const props = defineProps<{ source: SourceOverviewModel; profilePipelineId?: string }>();
const session = useHarvestConsole();
const isInstagram = computed(() => props.source.family === 'instagram');
const profileSchema = computed(() =>
  sourceProfileSchema(session.capabilities.value[props.source.sourceId]?.configuration.scopeSchema),
);
const usesPublisherProfile = computed(() => isInstagram.value || !!profileSchema.value?.fixedUrl);
const { mode, canManage, canChange, setMode } = useSourceModelProcessing(() => props.source.sourceId);
const processLabel = computed(() => ModelProcessingChoices.find((choice) => choice.value === mode.value)?.label);
const processNotes: Readonly<Record<ModelProcessingMode, string>> = {
  auto: 'New posts are queued for tagging automatically.',
  manual: 'Posts are processed when an operator requests it.',
  off: 'Model processing is off for this source.',
};
const sections = computed(() => props.source.sections);
const choices = computed(() =>
  sections.value.map(({ pipelineId }) => ({ value: pipelineId, label: session.sectionLabel(pipelineId) })),
);
const pipelineId = computed(() =>
  usesPublisherProfile.value
    ? ''
    : sections.value.some((item) => item.pipelineId === session.focusedPipelineId.value)
      ? session.focusedPipelineId.value
      : (sections.value[0]?.pipelineId ?? ''),
);
const { client, stages, loading, error, refresh } = usePipelineStages(pipelineId);
</script>

<template>
  <div v-if="usesPublisherProfile" class="grid min-w-0 gap-4">
    <SourceProfilePipelines :source="source" :schema="profileSchema" :pipeline-id="profilePipelineId" />
    <article v-if="isInstagram" class="grid min-w-0 gap-3 rounded-2xl border border-border bg-card p-4 sm:p-5">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h3 class="text-base font-semibold">Process</h3>
        <div v-if="canManage && mode" class="w-44 max-w-full">
          <ChoicePicker
            :model-value="mode"
            :choices="ModelProcessingChoices"
            :label="`Model processing for ${source.name}`"
            :is-disabled="!canChange"
            @update:model-value="setMode"
          />
        </div>
        <span v-else-if="processLabel" class="text-sm font-medium">{{ processLabel }}</span>
      </div>
      <p class="text-sm text-muted-foreground">
        {{ mode ? processNotes[mode] : 'Model processing status is unavailable.' }}
      </p>
      <ConsoleLink
        v-if="session.canReadProcessed.value"
        :to="{ kind: 'source', id: source.sourceId, tab: 'processed' }"
        class="text-sm text-primary underline-offset-2 hover:underline"
      >
        View processed posts
      </ConsoleLink>
    </article>
  </div>
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
