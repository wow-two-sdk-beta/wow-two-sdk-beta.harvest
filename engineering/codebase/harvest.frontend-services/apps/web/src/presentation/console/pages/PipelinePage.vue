<script setup lang="ts">
import { PipelinePanel, type PipelineTab } from '@wow-two-beta/fe-harvest/admin';
import type { PipelineStageKind } from '@wow-two-beta/fe-harvest/contracts';

defineProps<{ pipelineId: string; tab: PipelineTab; stage?: PipelineStageKind; runId?: string }>();
</script>

<template>
  <PipelinePanel
    :key="`${pipelineId}:${stage ?? ''}`"
    :pipeline-id="pipelineId"
    :tab="tab"
    v-bind="{ ...(stage ? { stage } : {}), ...(runId ? { runId } : {}) }"
  />
</template>
