<script setup lang="ts">
import { RunAuditPanel } from '@wow-two-beta/fe-harvest/admin';

const props = defineProps<{ sourceId: string; runId: string }>();
</script>

<template>
  <RunAuditPanel :source-id="props.sourceId" :run-id="props.runId" />
</template>
