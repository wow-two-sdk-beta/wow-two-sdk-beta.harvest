<script setup lang="ts">
import { PipelinePanel } from '@wow-two-beta/fe-harvest/admin';

/** Composes the shared pipeline panel: configuration, schedule and runs. */
defineOptions({ name: 'PipelinePage' });
const props = defineProps<{ pipelineId: string }>();
</script>

<template>
  <PipelinePanel :pipeline-id="props.pipelineId" />
</template>
